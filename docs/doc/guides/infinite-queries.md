# Infinite Queries

Infinite queries accumulate pages of data fetched sequentially with a **page parameter** — a cursor, offset, or page number. They share the same cache, deduplication, stale-while-revalidate, and invalidation guarantees as regular queries, but instead of holding a single `TData` value they hold an ordered list of pages and expose `FetchNextPage` / `FetchPreviousPage` for navigation.

## Creating an Infinite Query

Use `IQueryClient.CreateInfiniteQuery`:

```csharp
IInfiniteQuery<TArgs, TData, TPageParam> query =
    queryClient.CreateInfiniteQuery(new InfiniteQueryOptions<TArgs, TData, TPageParam>
    {
        KeyFactory       = args  => ...,
        Fetcher          = (args, page, ct) => ...,
        InitialPageParam = ...,
        GetNextPageParam = info  => ...,
    });
```

`TData` is the type of a **single page** (e.g. `List<PostDto>`). `TPageParam` is the type of the cursor or page identifier (e.g. `int`, `Guid`, `DateTimeOffset`).

### Required Options

**`KeyFactory`** — same as regular queries. Keys identify the cache entry; all observers with the same key share one `InfiniteQuery` instance.

```csharp
KeyFactory = listId => ["posts", listId]
```

**`Fetcher`** — receives args, the current page param, and a `CancellationToken`:

```csharp
Fetcher = (listId, page, ct) => api.GetPostsAsync(listId, page, PageSize, ct)
```

**`InitialPageParam`** — the page param passed to `Fetcher` on the very first fetch:

```csharp
InitialPageParam = 1       // offset / page-number pagination
InitialPageParam = Guid.Empty  // cursor-based, "before all items"
```

**`GetNextPageParam`** — returns the param for the next page, or `PageParam<TPageParam>.None` to signal there are no more pages. Receives an `InfinitePageInfo` containing the last loaded page and all accumulated pages:

```csharp
// Page-number: stop when we get fewer items than requested
GetNextPageParam = info =>
    info.Page.Count < PageSize
        ? PageParam<int>.None
        : PageParam<int>.Some(info.PageParam + 1)

// Cursor: use the last item's id
GetNextPageParam = info =>
    info.Page.LastOrDefault()?.Id is { } id
        ? PageParam<Guid>.Some(id)
        : PageParam<Guid>.None
```

### Optional Options

| Option | Default | Description |
|---|---|---|
| `GetPreviousPageParam` | `null` | Enables `FetchPreviousPage`. Return `None` when at the start. |
| `MaxPages` | `null` (unbounded) | Trims oldest pages when exceeded on `FetchNextPage`; trims newest on `FetchPreviousPage`. |
| `StaleTime` | global | Stale time for `Invalidate()` no-op check. |
| `CacheTime` | global | Time to keep the entry after the last subscriber leaves. |
| `RefetchInterval` | `null` | Automatic polling (triggers `RefetchAll`). |
| `RetryHandler` | global | Custom retry logic. |
| `IsEnabled` | `true` | When `false`, no fetches run. |
| `DataComparer` | `null` | Per-page equality check to suppress duplicate emissions. |
| `InitialData` | `null` | Pre-seeds the first page. The query starts in `Success` state and immediately displays the seeded data while a background refetch refreshes it. |
| `Name` | `null` | Low-cardinality name used to tag metrics. Falls back to the first part of the `QueryKey`. See [Observability](observability.md). |

### InitialData

`InitialData` lets you pre-populate the first page from a local cache, SSR payload, or any synchronous source. The query starts in `Success` state — no loading flash — and immediately triggers a background refetch to stay fresh.

```csharp
public readonly IInfiniteQuery<Guid, List<PostDto>, int> PostsQuery =
    queryClient.CreateInfiniteQuery(new InfiniteQueryOptions<Guid, List<PostDto>, int>
    {
        KeyFactory       = boardId => ["posts", boardId],
        Fetcher          = (boardId, page, ct) => api.GetPostsAsync(boardId, page, PageSize, ct),
        InitialPageParam = 1,
        GetNextPageParam = info =>
            info.Page.Count < PageSize ? PageParam<int>.None : PageParam<int>.Some(info.PageParam + 1),
        InitialData      = cachedFirstPage,   // IReadOnlyList<PostDto> from local storage / SSR
    });
```

Only the first page is seeded. Subsequent pages must be fetched via `FetchNextPage`. The seeded page uses `InitialPageParam` as its page parameter.

## PageParam\<TPageParam\>

`PageParam<TPageParam>` is a small discriminated union that avoids the C# generic nullable ambiguity for value-type page params.

```csharp
PageParam<int>.None        // no more pages
PageParam<int>.Some(2)     // next page param is 2
(PageParam<int>)2          // implicit conversion — equivalent to Some(2)
```

## Fetching Pages

### Initial Load

Call `SetArgs` to provide the args. The first fetch uses `InitialPageParam`:

```csharp
query.SetArgs(listId);
```

### FetchNextPage

Appends the next page. The page param comes from `GetNextPageParam` applied to the last loaded page. No-op when `HasNextPage` is `false` or no pages have been loaded yet.

```csharp
query.FetchNextPage();
```

During the fetch, `IsFetchingNextPage` is `true` and `Status` remains `Success` — existing pages stay visible.

### FetchPreviousPage

Prepends the previous page. Requires `GetPreviousPageParam` to be configured. No-op when `HasPreviousPage` is `false`.

```csharp
query.FetchPreviousPage();
```

### Invalidate / Refetch

Both re-fetch **all currently loaded pages** sequentially (rebuilding `Pages` and `PageParams` from scratch), matching TanStack Query behavior. Existing pages are carried in the `Fetching` state for stale-while-revalidate display.

```csharp
query.Invalidate(); // respects StaleTime
query.Refetch();    // always re-fetches all pages
```

Client-level invalidation targets infinite queries the same way as regular queries:

```csharp
queryClient.Invalidate(["posts", listId]);
queryClient.Invalidate(key => key.ToString().StartsWith("posts"));
```

## State

### InfiniteQueryState\<TData, TPageParam\>

Every state transition emits an immutable `InfiniteQueryState` snapshot:

```csharp
state.Status               // Idle | Fetching | Success | Failure
state.Pages                // IReadOnlyList<TData> — all loaded pages, oldest first
state.PageParams           // IReadOnlyList<TPageParam> — PageParams[i] fetched Pages[i]
state.Error                // Exception? — last fetch failure

state.HasNextPage          // true when GetNextPageParam returns Some(...)
state.HasPreviousPage      // true when GetPreviousPageParam returns Some(...)
state.IsFetchingNextPage   // true while FetchNextPage is in progress (Status stays Success)
state.IsFetchingPreviousPage // true while FetchPreviousPage is in progress

state.IsIdle               // Status == Idle
state.IsFetching           // Status == Fetching (full RefetchAll)
state.IsSuccess            // Status == Success
state.IsFailure            // Status == Failure
state.HasData              // Pages.Count > 0
state.HasError             // Error is not null
```

When `IsFetching` is true during a RefetchAll, the state still carries the previous `Pages` and `PageParams` so you can show stale content while rebuilding.

### Subscribing

```csharp
// All state transitions — replays current state to new subscribers
query.State.Subscribe(state =>
{
    var allItems = state.Pages.SelectMany(p => p).ToList();
    Render(allItems, state.HasNextPage, state.IsFetchingNextPage);
});

// Emits all pages after a full settle (not during IsFetchingNextPage/Previous)
query.Success.Subscribe(pages => Console.WriteLine($"Loaded {pages.Count} pages"));

// Exception on each failed fetch
query.Failure.Subscribe(e => ShowError(e));

// Final state after every fetch (success or failure)
query.Settled.Subscribe(_ => HideGlobalSpinner());
```

## Blazor Components

`<InfiniteTransition>` and `<InfiniteSuspense>` in `DotNetQuery.Blazor` handle subscriptions and re-rendering automatically. Both receive the full `InfiniteQueryState` as the `Content` context so the template can access pages, page counts, and loading flags in one place.

### \<InfiniteTransition\> (recommended)

Shows `Content` whenever at least one page is available — during background refetches and while loading more pages. Shows `Loading` only before the first page arrives.

```razor
<InfiniteTransition Query="_postsQuery">
    <Content Context="state">
        @foreach (var post in state.Pages.SelectMany(p => p))
        {
            <PostCard Post="post" />
        }

        @if (state.HasNextPage || state.IsFetchingNextPage)
        {
            <button @onclick="LoadMore" disabled="@state.IsFetchingNextPage">
                @(state.IsFetchingNextPage ? "Loading…" : "Load more")
            </button>
        }
    </Content>
    <Loading><LoadingSpinner /></Loading>
    <Failure Context="e"><ErrorMessage Message="@e.Message" /></Failure>
</InfiniteTransition>
```

### \<InfiniteSuspense\>

Shows `Content` only when `IsSuccess && HasData`. Reverts to `Loading` during full background refetches (same strict behavior as `<Suspense>`).

```razor
<InfiniteSuspense Query="_postsQuery">
    <Content Context="state">
        @foreach (var post in state.Pages.SelectMany(p => p))
        {
            <PostCard Post="post" />
        }
    </Content>
    <Loading><LoadingSpinner /></Loading>
    <Failure Context="e"><ErrorMessage Message="@e.Message" /></Failure>
</InfiniteSuspense>
```

| Scenario | InfiniteSuspense | InfiniteTransition |
|---|---|---|
| Initial load (no pages) | Loading | Loading |
| Full refetch (stale pages available) | Loading | Content |
| FetchNextPage in progress | Content | Content |
| Success | Content | Content |
| Failure (no pages) | Failure | Failure |
| Failure (has pages) | Failure | Content |

## Service/Facade Pattern

Keep infinite queries in a dedicated service class, consistent with regular queries:

```csharp
public sealed class PostsService(IQueryClient queryClient, IPostsApi api) : IDisposable
{
    private const int PageSize = 20;

    public readonly IInfiniteQuery<Guid, List<PostDto>, int> PostsQuery =
        queryClient.CreateInfiniteQuery(new InfiniteQueryOptions<Guid, List<PostDto>, int>
        {
            KeyFactory       = boardId => ["posts", boardId],
            Fetcher          = (boardId, page, ct) => api.GetPostsAsync(boardId, page, PageSize, ct),
            InitialPageParam = 1,
            GetNextPageParam = info =>
                info.Page.Count < PageSize ? PageParam<int>.None : PageParam<int>.Some(info.PageParam + 1),
            StaleTime = TimeSpan.FromMinutes(1),
        });

    public void Dispose() => PostsQuery.Dispose();
}
```

```razor
@page "/boards/{BoardId:guid}"
@inject PostsService Posts

<InfiniteTransition Query="Posts.PostsQuery">
    <Content Context="state">
        @foreach (var post in state.Pages.SelectMany(p => p))
        {
            <PostCard Post="post" />
        }
        @if (state.HasNextPage)
        {
            <button @onclick="() => Posts.PostsQuery.FetchNextPage()"
                    disabled="@state.IsFetchingNextPage">
                @(state.IsFetchingNextPage ? "Loading…" : "Load more")
            </button>
        }
    </Content>
    <Loading><p>Loading posts…</p></Loading>
    <Failure Context="e"><p>@e.Message</p></Failure>
</InfiniteTransition>

@code {
    [Parameter] public Guid BoardId { get; set; }

    protected override void OnParametersSet() => Posts.PostsQuery.SetArgs(BoardId);
}
```

## Cleaning Up

Infinite queries implement `IDisposable`. Dispose the query (or the service that owns it) when done:

```csharp
query.Dispose();
```

`<InfiniteTransition>` and `<InfiniteSuspense>` dispose their internal subscriptions automatically when the component is removed from the render tree.

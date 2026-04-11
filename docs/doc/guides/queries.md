# Queries

Queries are the core primitive in DotNet Query. A query represents a single async read operation with automatic caching, lifecycle management, and reactive state.

## Creating a Query

Use `IQueryClient.CreateQuery` to create a query:

```csharp
IQuery<TArgs, TData> query = queryClient.CreateQuery(new QueryOptions<TArgs, TData>
{
    KeyFactory = args => QueryKey.From(...),
    Fetcher    = (args, ct) => ...,
});
```

The two required properties are `KeyFactory` and `Fetcher`. Everything else is optional and overrides the global defaults set on `QueryClientOptions`.

### KeyFactory

The key factory derives a `QueryKey` from the args you push. Keys identify cache entries — two queries that produce the same key share the same cached data.

```csharp
// Simple key — always the same data regardless of args
KeyFactory = _ => QueryKey.From("config")

// Parameterized key — different data per id
KeyFactory = id => QueryKey.From("users", id)

// Compound key — different data per user and type
KeyFactory = (userId, type) => QueryKey.From("users", userId, "items", type)
```

Keep keys descriptive and hierarchical. Hierarchy matters for invalidation — invalidating `QueryKey.From("users")` with a predicate like `key => key.ToString().StartsWith("users")` can target all user-related entries at once.

### Fetcher

The fetcher is the async function that actually retrieves the data. It receives the args you pushed and a `CancellationToken` that is cancelled when:
- the query is disposed,
- a newer fetch supersedes this one (e.g. new args were pushed while fetching).

```csharp
Fetcher = (id, ct) => httpClient.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)
                      ?? throw new InvalidOperationException("User not found.")
```

Always pass the `CancellationToken` to your async operations. This ensures in-flight requests are cancelled cleanly when they are no longer needed.

### Per-Query Options

All global defaults can be overridden per query:

```csharp
new QueryOptions<int, UserDto>
{
    KeyFactory      = id => QueryKey.From("users", id),
    Fetcher         = (id, ct) => ...,
    StaleTime       = TimeSpan.FromMinutes(5),     // override global StaleTime
    CacheTime       = TimeSpan.FromMinutes(30),    // override global CacheTime
    RefetchInterval = TimeSpan.FromSeconds(60),    // poll every 60 seconds
    RetryHandler    = new MyCustomRetryHandler(),  // override global RetryHandler
    IsEnabled       = false,                       // start disabled
}
```

## Setting Args

Queries do not fetch until you set args. Call `SetArgs` to provide them:

```csharp
query.SetArgs(42);   // fetch user 42
query.SetArgs(99);   // switch to user 99 — cancels the in-flight fetch for 42 if still running
```

Every time you push new args, the query:
1. Derives the new key via `KeyFactory`.
2. Switches to the cache entry for that key (creating it if needed).
3. Triggers an `Invalidate()` on the new entry if the query is enabled.

Pushing the same args again does not automatically re-fetch if the data is still within stale time — use `Refetch()` for that.

## Query Lifecycle

A query moves through four states. Here is the full picture:

```
                  ┌──────────── Refetch() ────────────┐
                  │                                    │
 [no args] ── Idle ──── push args ──── Fetching ──── Success
                                          │               │
                                          │           Invalidate()
                                          │               │
                                        Failure ──── (retry) ─► Fetching
```

- **Idle** — initial state; no fetch has occurred for the current key.
- **Fetching** — a fetch is in progress. `LastData` carries forward any previously fetched data.
- **Success** — the fetch completed. `CurrentData` holds the fresh result; `LastData` is also updated.
- **Failure** — all retry attempts failed. `Error` holds the last exception; `LastData` still holds the previous successful data if there was one.

## Subscribing to State

### Full State Stream

`State` emits a `QueryState<TData>` on every transition. New subscribers immediately receive the current state (replay semantics):

```csharp
query.State.Subscribe(state =>
{
    switch (state.Status)
    {
        case QueryStatus.Idle:
            // nothing loaded yet
            break;
        case QueryStatus.Fetching:
            ShowSpinner();
            break;
        case QueryStatus.Success:
            Render(state.CurrentData!);
            break;
        case QueryStatus.Failure:
            ShowError(state.Error!);
            break;
    }
});
```

### Shortcut Streams

```csharp
// Emits only the unwrapped TData on each successful fetch
query.Success.Subscribe(data => Render(data));

// Emits only the Exception on each failed fetch
query.Failure.Subscribe(error => ShowError(error));

// Emits the final QueryState after every fetch (success or failure)
// Useful for hiding spinners regardless of outcome
query.Settled.Subscribe(_ => HideSpinner());
```

### Synchronous State Read

Read the current state without subscribing — handy for rendering checks:

```csharp
var state = query.CurrentState;

if (state.HasData)
    Render(state.CurrentData!);
```

### The LastData Pattern

`LastData` holds the result of the previous successful fetch and is carried forward across all subsequent state transitions. This is the foundation of stale-while-revalidate: while a background fetch is in progress, `LastData` still holds the old result so you can keep showing meaningful content.

```csharp
query.State.Subscribe(state =>
{
    // Show stale data while fetching, fresh data on success
    var displayData = state.CurrentData ?? state.LastData;

    if (displayData is not null)
        Render(displayData);
    else if (state.IsFetching)
        ShowSpinner();
});
```

The [Blazor `<Transition>` component](blazor.md) applies this pattern automatically.

## Controlling Queries

### Refetch

`Refetch()` triggers an immediate fetch, bypassing stale-time entirely:

```csharp
query.Refetch(); // always fetches, even if data was just loaded
```

Use this for user-initiated refresh (e.g. a "Refresh" button).

### Invalidate

`Invalidate()` marks the cached data as stale. What happens next depends on whether there are active subscribers:

- **With active subscribers** — a fetch starts immediately.
- **Without active subscribers** — the entry is marked stale; the fetch is deferred until the next subscriber joins.

```csharp
query.Invalidate();
```

The difference from `Refetch()` is that `Invalidate()` respects stale time — if the data was fetched within the stale-time window, it is a no-op. `Refetch()` always fetches.

### Cancel

`Cancel()` stops the currently running fetch and returns the query to `Idle`:

```csharp
query.Cancel();
```

`LastData` is preserved after cancellation.

### SetEnabled

Disable the query to prevent any fetches from occurring:

```csharp
query.SetEnabled(false); // all fetches suspended
// ... some time later ...
query.SetEnabled(true);  // re-enables; triggers Invalidate() immediately if there are subscribers
```

This is useful for conditional queries — for example, only fetching when a user is selected:

```csharp
// Start disabled; enable when we have a valid ID
var query = queryClient.CreateQuery(new QueryOptions<int?, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => ...,
    IsEnabled  = false,
});

// Later, when the user selects an ID:
query.SetEnabled(true);
query.SetArgs(selectedUserId);
```

### Detach

`Detach()` removes the query from the client cache while keeping existing subscriptions alive. The query continues to work, but it will no longer be returned or shared by future calls to `CreateQuery` with the same key.

```csharp
query.Detach();
```

This is different from `Dispose()`. Disposing also tears down all subscriptions and releases resources.

## Cleaning Up

Queries implement `IDisposable`. Dispose the query when you are done with it:

```csharp
query.Dispose();
```

In Blazor components, implement `IDisposable` on the component and dispose in `Dispose()`. The Blazor components (`<Suspense>`, `<Transition>`) handle this automatically for their internal subscriptions.

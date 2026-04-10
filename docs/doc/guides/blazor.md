# Blazor Components

DotNet Query ships two Razor components that make it easy to render query state declaratively in Blazor: `<Suspense>` and `<Transition>`. Both components handle subscriptions and re-rendering automatically — you just describe what to show in each state.

## Installation

Make sure the Blazor package is installed:

```bash
dotnet add package DotNetQuery.Blazor
```

Then add the namespace to your `_Imports.razor`:

```razor
@using DotNetQuery.Blazor
```

## Suspense

`<Suspense>` is the straightforward component. It shows a loading indicator while fetching, the data when successful, and an error template on failure. While a background refetch is in progress, it shows the loading template — the old data is hidden.

```razor
<Suspense Query="_userQuery">
    <Content Context="user">
        <h1>@user.Name</h1>
        <p>@user.Email</p>
    </Content>
    <Loading>
        <p>Loading user...</p>
    </Loading>
    <Failure Context="error">
        <p class="error">Could not load user: @error.Message</p>
    </Failure>
</Suspense>
```

### Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Query` | `IQuery<TArgs, TData>` | Yes | The query to render. |
| `Content` | `RenderFragment<TData>` | Yes | Rendered when the query succeeds. |
| `Loading` | `RenderFragment` | No | Rendered while `Idle` or `Fetching`. Defaults to nothing. |
| `Failure` | `RenderFragment<Exception>` | No | Rendered on `Failure`. Defaults to nothing. |

### When to Use Suspense

Use `<Suspense>` when:
- you want a clean loading state between navigations (no stale data flash),
- the data is critical and you do not want to show outdated content,
- you prefer explicit "loading…" states over stale-while-revalidate.

## Transition

`<Transition>` applies **stale-while-revalidate** semantics: while a background fetch is in progress it keeps showing the last successful data instead of switching to a loading indicator. Only when there is no previous data at all does it fall back to the loading template.

```razor
<Transition Query="_productListQuery">
    <Content Context="products">
        @foreach (var product in products)
        {
            <ProductCard Product="product" />
        }
    </Content>
    <Loading>
        <p>Loading products...</p>
    </Loading>
    <Failure Context="error">
        <p class="error">@error.Message</p>
    </Failure>
</Transition>
```

### Parameters

Same as `<Suspense>`:

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `Query` | `IQuery<TArgs, TData>` | Yes | The query to render. |
| `Content` | `RenderFragment<TData>` | Yes | Rendered when data is available (current or last). |
| `Loading` | `RenderFragment` | No | Rendered only when there is no data at all. |
| `Failure` | `RenderFragment<Exception>` | No | Rendered on `Failure` when no previous data exists. |

### When to Use Transition

Use `<Transition>` when:
- the data updates frequently and you want smooth background refreshes,
- switching to a loading spinner on every refetch would feel jarring,
- you are showing a list that is periodically re-fetched.

## Suspense vs Transition at a Glance

| Scenario | Suspense | Transition |
|----------|----------|------------|
| Initial load (no data yet) | Shows `Loading` | Shows `Loading` |
| Background refetch (has old data) | Shows `Loading` | Shows old `Content` |
| Success | Shows `Content` | Shows `Content` |
| Failure (has old data) | Shows `Failure` | Shows old `Content` |
| Failure (no old data) | Shows `Failure` | Shows `Failure` |

## Complete Component Example

Here is a full Blazor component using both queries and mutations with the Blazor components:

```razor
@page "/users/{Id:int}"
@inject IQueryClient QueryClient
@inject HttpClient Http
@implements IDisposable

<Transition Query="_userQuery">
    <Content Context="user">
        <h1>@user.Name</h1>

        <button @onclick="HandleRefresh">Refresh</button>

        <Suspense Query="_postsQuery">
            <Content Context="posts">
                <ul>
                    @foreach (var post in posts)
                    {
                        <li>@post.Title</li>
                    }
                </ul>
            </Content>
            <Loading><p>Loading posts...</p></Loading>
        </Suspense>
    </Content>
    <Loading><p>Loading user...</p></Loading>
    <Failure Context="error"><p>Error: @error.Message</p></Failure>
</Transition>

@code {
    [Parameter] public int Id { get; set; }

    private IQuery<int, UserDto>         _userQuery  = default!;
    private IQuery<int, List<PostDto>>   _postsQuery = default!;

    protected override void OnInitialized()
    {
        _userQuery = QueryClient.CreateQuery(new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => Http.GetFromJsonAsync<UserDto>($"/api/users/{id}", ct)!,
            StaleTime  = TimeSpan.FromMinutes(5),
        });

        _postsQuery = QueryClient.CreateQuery(new QueryOptions<int, List<PostDto>>
        {
            KeyFactory = id => QueryKey.From("users", id, "posts"),
            Fetcher    = (id, ct) => Http.GetFromJsonAsync<List<PostDto>>($"/api/users/{id}/posts", ct)!,
        });
    }

    protected override void OnParametersSet()
    {
        _userQuery.Args.OnNext(Id);
        _postsQuery.Args.OnNext(Id);
    }

    private void HandleRefresh() => _userQuery.Refetch();

    public void Dispose()
    {
        _userQuery.Dispose();
        _postsQuery.Dispose();
    }
}
```

## Tips

- **Dispose your queries.** Both `<Suspense>` and `<Transition>` dispose their internal subscriptions automatically, but they do not dispose the query itself. Dispose the query in your component's `Dispose()` method (or `DisposeAsync()`).
- **Push args in `OnParametersSet`.** When your component receives route parameters, push them inside `OnParametersSet` so the query updates when the URL changes.
- **Nest components freely.** A `<Suspense>` inside a `<Transition>`'s `Content` works perfectly — each component manages its own subscription independently.

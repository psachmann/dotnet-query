# Prefetching

Prefetching warms the cache before any component subscribes to a query. When a component mounts and calls `SetArgs`, it receives the cached data immediately — no loading state, no flash of empty content.

## The Facade Pattern

The recommended way to use DotNet Query is to create queries and mutations in a dedicated service or facade, then inject that class into your components. Prefetching fits naturally into this model: the facade owns the query, so it is the right place to call `PrefetchAsync`.

```csharp
public class UserFacade
{
    private readonly IUserApi _api;

    public IQuery<int, UserDto> UserQuery { get; }

    public UserFacade(IQueryClient client, IUserApi api)
    {
        _api = api;
        UserQuery = client.CreateQuery(new QueryOptions<int, UserDto>
        {
            KeyFactory = id => QueryKey.From("users", id),
            Fetcher    = (id, ct) => _api.GetUserAsync(id, ct),
            StaleTime  = TimeSpan.FromMinutes(5),
        });
    }
}
```

With this setup, a component only needs to inject `UserFacade` — not `IQueryClient` — and has access to both the query and `PrefetchAsync`.

## Warming the Cache

Call `PrefetchAsync` on the query with the args to prefetch:

```csharp
// Warm the cache before the detail component mounts
await facade.UserQuery.PrefetchAsync(userId);
```

If the cache already holds fresh data for that key (within stale time), the fetch is skipped. This makes it safe to call `PrefetchAsync` speculatively without worrying about redundant requests.

A common place to trigger prefetching is during navigation — for example, in a list component when the user hovers over an item:

```csharp
private async Task OnItemHover(int userId)
{
    await _facade.UserQuery.PrefetchAsync(userId);
}
```

By the time the user clicks and the detail component mounts, the data is already in the cache.

## Prefetching Multiple Keys

A single query instance can prefetch multiple keys at once. The query's `KeyFactory` handles key derivation for each set of args independently:

```csharp
// Warm the first page of results in parallel
await Task.WhenAll(
    facade.UserQuery.PrefetchAsync(1),
    facade.UserQuery.PrefetchAsync(2),
    facade.UserQuery.PrefetchAsync(3)
);
```

Each key gets its own cache entry. There is no cross-contamination between entries.

## Cancellation

`PrefetchAsync` accepts an optional `CancellationToken`:

```csharp
await facade.UserQuery.PrefetchAsync(userId, cancellationToken);
```

If the token is cancelled while the fetch is in-flight, the fetch is abandoned and the cache entry is left in `Idle` state. `PrefetchAsync` does not throw on cancellation.

## Interaction with Stale Time

Prefetching stamps the cache entry as fresh. When a component later subscribes and calls `SetArgs` with the same args:

- If the data is still within the stale-time window, no fetch is triggered — the component sees `Success` immediately.
- If the stale time has elapsed by the time the component mounts, a background refetch is triggered.

Setting a meaningful `StaleTime` on queries that you intend to prefetch gives you control over how long the prefetched data is served without a refetch.

## No-Subscriber Guarantee

Unlike `Invalidate()`, which defers the fetch until the first subscriber joins, `PrefetchAsync` always fetches immediately regardless of whether there are subscribers. This is what makes it suitable for cache warming ahead of rendering.

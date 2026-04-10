# Caching

The cache is one of the most powerful parts of DotNet Query. Understanding how it works helps you tune your app's data freshness and memory usage.

## How the Cache Works

The cache lives inside `IQueryClient` and is keyed by `QueryKey`. Every time you push args to a query, the client:

1. Derives the key via your `KeyFactory`.
2. Looks up the cache entry for that key.
3. If no entry exists, creates one and starts a fetch.
4. If an entry exists, returns the cached state and decides whether a refetch is needed.

All queries that produce the same key share the same cache entry — they will never trigger duplicate fetches. This is called **query deduplication**.

## Stale Time

Stale time controls how long fetched data is considered "fresh". Within the stale-time window, `Invalidate()` is a no-op — the cached data is served as-is.

```
                StaleTime window
fetch ──────────┤                ├──────── data is stale
                │     fresh      │
                │  Invalidate()  │
                │   is no-op     │
```

The default stale time is `TimeSpan.Zero`, meaning data is considered stale immediately after it is fetched. In most applications this is fine — the data is still served from cache on re-subscribe; stale time just prevents redundant refetches when something is re-rendered quickly.

Setting a longer stale time is useful for data that changes infrequently:

```csharp
// Global: all queries use 1 minute stale time
builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(1);
});

// Per-query override
new QueryOptions<int, ConfigDto>
{
    KeyFactory = _ => QueryKey.From("config"),
    Fetcher    = (_, ct) => configService.LoadAsync(ct),
    StaleTime  = TimeSpan.FromHours(1), // config changes rarely
}
```

> `Refetch()` always bypasses stale time and forces an immediate fetch.

## Cache Time

Cache time controls how long data stays in memory **after the last subscriber unsubscribes**. This is independent of stale time.

```
last subscriber unsubscribes
         │
         │◄── CacheTime ──►│
         │                 │
         │   data lives    │ entry evicted
         │   in memory     │
```

The default cache time is 5 minutes. During this window, if a new subscriber joins:
- they immediately receive the cached state,
- a fetch is triggered if the data is stale.

After the cache time elapses with no subscribers, the entry is evicted and the next subscriber will start fresh.

```csharp
// Keep data for 30 minutes after all subscribers leave
new QueryOptions<int, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => ...,
    CacheTime  = TimeSpan.FromMinutes(30),
}
```

Setting `CacheTime = TimeSpan.Zero` means data is evicted immediately when the last subscriber unsubscribes.

## Query Deduplication

When two different parts of your app create queries with the same options, they share the same underlying cache entry. Only one fetch ever runs at a time for a given key.

```csharp
// Component A
var queryA = queryClient.CreateQuery(options); // same options as Component B
queryA.Args.OnNext(42);                        // triggers a fetch

// Component B (maybe rendered at the same time)
var queryB = queryClient.CreateQuery(options);
queryB.Args.OnNext(42);                        // cache hit — no second fetch
```

Both `queryA` and `queryB` receive the same `QueryState<TData>` transitions. This eliminates redundant network requests and keeps your data consistent.

## Invalidation

Invalidation marks one or more cache entries as stale. If they have active subscribers, a refetch is triggered immediately. If not, the fetch is deferred until the next subscriber joins.

### Invalidate by Key

```csharp
queryClient.Invalidate(QueryKey.From("users", 42));
```

This only matches the exact key `users:42`.

### Invalidate by Predicate

```csharp
// Invalidate all user-related entries
queryClient.Invalidate(key => key.ToString().StartsWith("users"));

// Invalidate everything
queryClient.Invalidate(_ => true);
```

Predicate invalidation is useful after mutations that affect multiple queries.

### Invalidating from a Query Instance

You can also invalidate a specific query directly:

```csharp
query.Invalidate(); // respects stale time
query.Refetch();    // ignores stale time — always fetches
```

## Automatic Refetch Interval

Set `RefetchInterval` to have queries automatically poll in the background while they have active subscribers:

```csharp
new QueryOptions<Unit, DashboardDto>
{
    KeyFactory      = _ => QueryKey.From("dashboard"),
    Fetcher         = (_, ct) => dashboardService.GetAsync(ct),
    RefetchInterval = TimeSpan.FromSeconds(30), // refresh every 30 seconds
}
```

The interval only runs while there are active subscribers. When the last subscriber unsubscribes, polling stops and the cache-time clock starts.

## Choosing the Right Settings

Here is a quick reference for common scenarios:

| Scenario | StaleTime | CacheTime | RefetchInterval |
|----------|-----------|-----------|-----------------|
| User profile (changes rarely) | 5–10 min | 30 min | — |
| Live dashboard | 0 | 1 min | 10–30 sec |
| Config / feature flags | 1 hour | 1 hour | — |
| Search results | 0 | 0 | — |
| Notifications | 0 | 5 min | 30 sec |

These are starting points — tune them based on how often your data actually changes and what latency your users expect.

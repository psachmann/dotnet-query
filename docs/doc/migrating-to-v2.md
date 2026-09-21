# Migrating from v1 to v2

This guide covers what changes when you upgrade from DotNet Query 1.x (last stable release: 1.3.0) to 2.0. Most applications only need the first section. The rest matters if you implement the library's interfaces yourself, rely on cache entries outliving their subscribers, or have dashboards built on the library's telemetry.

## At a Glance

| Change | Affects you if… | What to do |
|---|---|---|
| [Query data must be a reference type](#query-data-must-be-a-reference-type) | a query returns `int`, `bool`, `DateTime`, a `struct`, … | Wrap the value in a record |
| [New interface members](#new-members-on-iqueryclient-and-imutation) | you implement `IQueryClient` or `IMutation` yourself | Implement the new members |
| [`AddDotNetQuery` keeps the first registration](#adddotnetquery-no-longer-replaces-an-existing-registration) | you call it more than once, or register `IQueryClient` yourself | Register once |
| [`QueryKey.Default` is a private sentinel](#querykeydefault-no-longer-equals-querykeyfrom0) | you compare keys against `QueryKey.From("\0")` | Compare against `QueryKey.Default` |
| [Unused cache entries are evicted](#unused-cache-entries-are-evicted-automatically) | you expect data to stay cached with no subscribers | Raise `CacheTime` if needed |
| [`RefetchInterval` honours `StaleTime`](#refetchinterval-honours-staletime-and-subscribers) | a polling query's `StaleTime` is not shorter than its interval | Lower `StaleTime` |
| [Telemetry units and tags](#telemetry-changes) | you have dashboards or alerts on DotNet Query metrics or logs | Update queries and histogram buckets |

## Update the Packages

The packages depend on each other at matching versions, so upgrade all of them together:

```bash
dotnet add package DotNetQuery.Core
dotnet add package DotNetQuery.Extensions.DependencyInjection
dotnet add package DotNetQuery.Blazor
dotnet add package DotNetQuery.Blazor.DevTools
dotnet add package DotNetQuery.Mvvm
```

While 2.0 is in preview, add `--prerelease` to each command or pass an explicit `--version`.

Then rebuild. Every source-breaking change below produces a compiler error, so the build output is your checklist for that part. Behaviour and telemetry changes compile silently — read those sections even if the build is clean.

---

## Breaking Changes

### Query data must be a reference type

`TData` is now constrained to `class` on `IQuery<TArgs, TData>`, `QueryOptions<TArgs, TData>`, `QueryState<TData>`, `IQueryClient.CreateQuery`, `<Suspense>`, and `<Transition>`. Queries that return a DTO, `string`, array, or collection compile unchanged. A query that returns a value type no longer builds:

```
error CS0452: The type 'int' must be a reference type in order to use it as parameter 'TData'
in the generic type or method 'QueryOptions<TArgs, TData>'
```

Nullable value types such as `int?` are structs too, so they don't satisfy the constraint either.

**Why:** the library uses `null` to mean "no data yet". A value type has no such value, so in 1.x a query of `int` reported `HasData == true` and `CurrentData == 0` before its first fetch had completed.

**How to migrate:** wrap the value in a small record.

Before:

```csharp
var unread = queryClient.CreateQuery(new QueryOptions<int, int>
{
    KeyFactory = userId => QueryKey.From("unread", userId),
    Fetcher    = (userId, ct) => api.GetUnreadCountAsync(userId, ct),
});

unread.Success.Subscribe(count => badge.Text = count.ToString());
```

After:

```csharp
public sealed record UnreadCount(int Value);

var unread = queryClient.CreateQuery(new QueryOptions<int, UnreadCount>
{
    KeyFactory = userId => QueryKey.From("unread", userId),
    Fetcher    = async (userId, ct) => new UnreadCount(await api.GetUnreadCountAsync(userId, ct)),
});

unread.Success.Subscribe(count => badge.Text = count.Value.ToString());
```

A record keeps value equality, so the default `DataComparer` (`EqualityComparer<TData>.Default`) still compares results by value.

In Blazor, `<Suspense>` and `<Transition>` infer `TData` from the query, so once the query compiles only the template changes — `@count` becomes `@count.Value`.

Mutations are not affected: `MutationOptions<TArgs, TData>` and `IMutation<TArgs, TData>` still accept any `TData`.

### New members on `IQueryClient` and `IMutation`

Two interfaces gained members. This only affects code that implements them itself — decorators, wrappers, or hand-written test fakes. Mocking libraries pick the new members up automatically.

| Interface | New member |
|---|---|
| `IQueryClient` | `IInfiniteQuery<TArgs, TData, TPageParam> CreateInfiniteQuery<TArgs, TData, TPageParam>(InfiniteQueryOptions<TArgs, TData, TPageParam> options) where TData : class` |
| `IMutation<TArgs, TData>` | `MutationState<TData> CurrentState { get; }` |

A decorator forwards the new method like the existing ones. Note that `CreateQuery` also needs the new `where TData : class` constraint:

```csharp
public sealed class LoggingQueryClient(IQueryClient inner) : IQueryClient
{
    public IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options)
        where TData : class => inner.CreateQuery(options);

    public IInfiniteQuery<TArgs, TData, TPageParam> CreateInfiniteQuery<TArgs, TData, TPageParam>(
        InfiniteQueryOptions<TArgs, TData, TPageParam> options
    )
        where TData : class => inner.CreateInfiniteQuery(options);

    // CreateMutation, Invalidate, and Dispose are unchanged.
}
```

A custom `IMutation` must expose its latest state synchronously through `CurrentState`, set to `Running` before `Execute` returns.

### `AddDotNetQuery` no longer replaces an existing registration

`AddDotNetQuery` now registers `IQueryClient` with `TryAdd` instead of `Add`, so it does nothing if an `IQueryClient` is already registered:

- **Calling `AddDotNetQuery` twice** — the **first** call's options now win. In 1.x the last call won. Options passed to a later call are still validated, then ignored.
- **Registering `IQueryClient` yourself before calling `AddDotNetQuery`** — your registration is now kept instead of being overridden.

**How to migrate:** configure the client in a single `AddDotNetQuery` call. To replace the client in a test host, remove the existing registration first:

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;

services.RemoveAll<IQueryClient>();
services.AddDotNetQuery(options => options.StaleTime = TimeSpan.Zero);
```

### `QueryKey.Default` no longer equals `QueryKey.From("\0")`

`QueryKey.Default` — the key an `IQuery` reports before its first `SetArgs` — is now built from a private marker type, so no key you construct can collide with it. Its `ToString()` is `<uninitialized>`.

If you detected an uninitialised query by comparing against `QueryKey.From("\0")` or by inspecting the key's string, compare against the sentinel instead:

```csharp
if (query.Key == QueryKey.Default)
{
    // SetArgs has not been called yet
}
```

---

## Behaviour Changes

These compile without errors but can change what your application does at runtime.

### Unused cache entries are evicted automatically

In 1.x, the `CacheTime` countdown only started when you called `Detach()`. Otherwise a cache entry — its data, and any `RefetchInterval` timer — lived until the client was disposed. That included entries left behind whenever `SetArgs` switched a query to a different key.

In 2.0, the countdown starts as soon as the **last subscriber** to a cache entry leaves. Subscribers include `State`, `Success`, `Failure`, and `Settled` subscriptions, as well as the Blazor components and view models built on them. A new subscriber arriving before the countdown ends cancels it. Once it elapses, the entry is disposed and the next subscriber starts with a fresh fetch. This is the behaviour the [Caching guide](guides/caching.md) describes; 1.x only applied it after `Detach()`.

**What to do:** usually nothing — memory use no longer grows with every key a query has visited. If you relied on data surviving a long gap with no subscribers (for example, returning to a page after several minutes), raise `CacheTime` for those queries. `Detach()` still works and starts the countdown immediately, but you no longer need it for cleanup.

### `RefetchInterval` honours `StaleTime` and subscribers

In 1.x, every interval tick started a fetch, whether or not anyone was subscribed and however fresh the data was.

In 2.0, a tick behaves like calling `Invalidate()`:

- While the data is still within `StaleTime`, the tick is skipped.
- With no subscribers, the query is only marked stale and fetches when the next subscriber arrives.

**What to do:** if a polling query's `StaleTime` (its own, or the global default) is as long as or longer than its `RefetchInterval`, ticks are skipped and polling slows to roughly the `StaleTime` pace. Give polling queries a `StaleTime` comfortably below the interval:

```csharp
new QueryOptions<string, PriceDto>
{
    KeyFactory      = symbol => QueryKey.From("prices", symbol),
    Fetcher         = (symbol, ct) => prices.GetAsync(symbol, ct),
    RefetchInterval = TimeSpan.FromSeconds(30),
    StaleTime       = TimeSpan.FromSeconds(20), // below the interval, so every tick refetches
}
```

### Fixes you may notice

- **`Cancel()` no longer breaks the query.** In 1.x it cancelled the cache entry's token permanently, so every later fetch was cancelled as soon as it started. If you worked around this by disposing and recreating queries after cancelling, you can remove the workaround.
- **`SetArgs` no longer leaks.** Switching to args whose key was already cached left a discarded query instance running in the background.
- **Blazor teardown is clean.** `<QueryRefreshMonitor>` now removes its `visibilitychange` and `online` listeners when disposed, and neither it nor `<QueryDevTools>` throws `JSDisconnectedException` when a Blazor Server circuit closes.

---

## Telemetry Changes

The `ActivitySource` and `Meter` name (`QueryTelemetry.SourceName`) and the span names (`query.fetch`, `mutation.execute`) are unchanged. What changed is units, tags, and a few log messages. See the [Observability guide](guides/observability.md) for the complete 2.0 reference.

### Duration histograms record seconds

`dotnetquery.query.duration` and `dotnetquery.mutation.duration` now use the unit `s` instead of `ms`, following the OpenTelemetry semantic conventions. Recorded values are 1000× smaller.

- Update alert thresholds and dashboard queries.
- Exporters that append the unit to the metric name will expose a new series name. For example, with the Prometheus exporter `dotnetquery_query_duration_milliseconds` becomes `dotnetquery_query_duration_seconds`.
- The OpenTelemetry .NET SDK's default histogram buckets are sized for milliseconds (`0, 5, 10, 25, … 10000`), so every duration under five seconds lands in the same bucket and percentiles become meaningless. Configure explicit boundaries:

```csharp
builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics
    .AddMeter(QueryTelemetry.SourceName)
    .AddView("dotnetquery.query.duration", new ExplicitBucketHistogramConfiguration
    {
        Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
    })
    .AddView("dotnetquery.mutation.duration", new ExplicitBucketHistogramConfiguration
    {
        Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
    }));
```

Log messages still report durations in milliseconds.

### Query metrics are tagged with `query.name` instead of `query.key`

`dotnetquery.query.duration`, `dotnetquery.query.active`, `dotnetquery.cache.hits`, and `dotnetquery.cache.misses` no longer carry the full `query.key` tag. They carry `query.name` instead: the query's `Name` option, or the first part of its key when `Name` is unset. A series that was split by `query.key="users:42"`, `query.key="users:43"`, … is now a single `query.name="users"` series.

This keeps metric cardinality bounded — a key usually contains per-entity arguments such as an id, which produced one time series per distinct value. Traces and log messages still carry the full key.

To name a query explicitly:

```csharp
new QueryOptions<int, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => users.GetAsync(id, ct),
    Name       = "users",
}
```

If your keys come from a small, fixed set and you want per-key series back, set `IncludeQueryKeyInMetrics`. It adds `query.key` alongside `query.name`:

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.IncludeQueryKeyInMetrics = true;
});
```

### New tags and tag values

- `dotnetquery.query.duration` now also records cancelled fetches, with `status="cancelled"`, and gains `error.type` (on failure) and `trigger`. If you compute an error rate as failures divided by all recordings, decide whether cancelled fetches belong in the denominator.
- `dotnetquery.mutation.duration` gains `mutation.name` (the mutation's `Name` option, or `typeof(TArgs).Name`), `status="cancelled"`, and `error.type`.
- New instruments: `dotnetquery.cache.entries`, `dotnetquery.cache.evictions`, `dotnetquery.query.retries`, and `dotnetquery.mutation.retries`.
- Spans gain tags such as `query.name`, `trigger`, `attempts`, and `mutation.name`. Existing span tags are unchanged.

### Mutation log messages include the mutation name

The mutation log templates now include the name: `Mutation started` became `Mutation '{MutationName}' started`, and the same applies to the succeeded, failed, and cancelled messages. Update any log queries or alerts that match on the old message text. Query log messages are unchanged.

---

## New in 2.0

None of these require changes to existing code, but they are worth knowing about once you have upgraded:

- **Infinite queries** — `IQueryClient.CreateInfiniteQuery` with `<InfiniteSuspense>` and `<InfiniteTransition>` for paginated and "load more" lists. See [Infinite Queries](guides/infinite-queries.md).
- **MVVM view models** — the new `DotNetQuery.Mvvm` package brings bindable `QueryViewModel`, `InfiniteQueryViewModel`, and `MutationViewModel` types to MAUI, WPF, WinUI, UNO Platform, and Avalonia. See [MVVM View Models](guides/mvvm.md).
- **Named queries and mutations** — the `Name` option on `QueryOptions` and `MutationOptions` controls how they appear in metrics, traces, and logs. See [Observability](guides/observability.md).
- **`IMutation.CurrentState`** — read a mutation's state synchronously, for example to prevent a double submit while a UI update is still being dispatched.

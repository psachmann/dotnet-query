# Migrating from v2 to v3

> **3.0 is not released yet.** This guide tracks the breaking changes merged so far and grows until the release. The current stable version is 2.0.0.

This guide covers what changes when you upgrade from DotNet Query 2.x (last stable release: 2.0.0) to 3.0.

## At a Glance

| Change | Affects you if… | What to do |
|---|---|---|
| [.NET 9 is no longer supported](#net-9-is-no-longer-supported) | your application targets `net9.0` | Move to .NET 10, or stay on 2.0.0 |
| [New members on `IQueryClient`](#new-members-on-iqueryclient) | you implement `IQueryClient` yourself | Implement the new members |

## Breaking Changes

### .NET 9 is no longer supported

All packages now target `net10.0` only. 2.x shipped `net9.0` and `net10.0` assemblies side by side; 3.0 drops the `net9.0` one, so a project targeting `net9.0` fails at restore:

```
error NU1202: Package DotNetQuery.Core 3.0.0 is not compatible with net9.0 (.NETCoreApp,Version=v9.0).
```

**Why:** .NET 9 reaches end of support on 10 November 2026, and targeting .NET 10 alone lets the library use .NET 10 APIs without fallbacks.

**How to migrate:** retarget your application to `net10.0`. There is no API change tied to this — code that built against the `net10.0` assemblies of 2.0.0 is unaffected.

If you have to stay on .NET 9, stay on 2.0.0. It is the last release that supports .NET 9, and there will be no further 2.x releases.

### New members on `IQueryClient`

`IQueryClient` gained members. This only affects code that implements it itself — decorators, wrappers, or hand-written test fakes. Mocking libraries pick the new members up automatically. An implementation written against 2.0.0 no longer builds:

```
error CS0535: 'LoggingQueryClient' does not implement interface member 'IQueryClient.CreateMutation<TArgs, TData, TProgress>(MutationOptions<TArgs, TData, TProgress>)'
error CS0535: 'LoggingQueryClient' does not implement interface member 'IQueryClient.CreateStreamedQuery<TArgs, TData, TChunk>(StreamedQueryOptions<TArgs, TData, TChunk>)'
```

| Interface | New member |
|---|---|
| `IQueryClient` | `IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(MutationOptions<TArgs, TData, TProgress> options)` |
| `IQueryClient` | `IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(StreamedQueryOptions<TArgs, TData, TChunk> options) where TData : class` |

**Why:** mutations can now report progress (see [Reporting Progress](guides/mutations.md#reporting-progress)). A progress-reporting mutation has its own options, state, and interface types, so the client needs a factory method for it, beside the existing one. The client can also now create [streamed queries](guides/streamed-queries.md), whose fetcher yields chunks that are folded into the data as they arrive. They have their own options type, so the client needs a factory method for them too.

**How to migrate:** a decorator forwards the new methods like the existing ones:

```csharp
public sealed class LoggingQueryClient(IQueryClient inner) : IQueryClient
{
    public IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(
        MutationOptions<TArgs, TData, TProgress> options
    ) => inner.CreateMutation(options);

    public IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(
        StreamedQueryOptions<TArgs, TData, TChunk> options
    )
        where TData : class => inner.CreateStreamedQuery(options);

    // CreateQuery, CreateInfiniteQuery, the existing CreateMutation, Invalidate, and Dispose are unchanged.
}
```

## Behaviour Changes

These compile without errors but can change what your application does at runtime.

### Fixes you may notice

- **Executing a mutation again while it runs no longer flickers its state.** `Execute` while a run is in flight cancels that run in favour of the new one. In 2.x the cancelled run still published `Idle` (or its result, if the mutator ignored the token) once it wound down, so the mutation briefly looked idle — or finished — while the new run was still going, and a command gated on `IsRunning` could re-enable early. A superseded run now leaves `State` alone — and therefore also `Success`, `Failure`, `Settled`, and `InvalidateKeys`, which are driven by it. Its callbacks still fire as before: `OnSettled` always, and `OnSuccess` / `OnFailure` if the mutator ignored the token and completed anyway. If you relied on that stray emission — for example, waiting for `Idle` after the second `Execute` — wait for the second run's `Settled` instead.
- **Invalidating a query no longer restarts a fetch that is already in flight.** In 2.x every invalidation — `Invalidate`, a mutation's `InvalidateKeys`, a `RefetchInterval` tick, `<QueryRefreshMonitor>` on focus or reconnect — cancelled the running fetch and started over, so a `RefetchInterval` shorter than the fetch took meant the query never left `Fetching`. The running fetch now finishes and publishes its result. Because it may predate whatever caused the invalidation, `Invalidate` and `InvalidateKeys` are followed by one more fetch once it settles; an interval tick is simply skipped. A second observer attaching to a key that is already fetching shares that fetch instead of restarting it. `Refetch()` still cancels and restarts. If you counted fetcher calls or relied on the cancellation, expect the first fetch to complete.
- **A superseded fetch can no longer overwrite newer data.** A fetcher that ignores its `CancellationToken` keeps running after `Refetch()`, `Cancel()` or a new page command has replaced it. In 2.x its late result was still published and marked fresh, so the older response could win; an infinite query could append a page to a list that had been refetched in the meantime. Such a result is now discarded. Pass the token through to your HTTP call anyway — it is what actually stops the request.
- **A disabled query is no longer fetched by invalidation.** `SetEnabled(false)` (or `IsEnabled = false`) was only honoured by `SetArgs`; `Invalidate`, `IQueryClient.Invalidate`, `RefetchInterval` and `<QueryRefreshMonitor>` fetched it regardless. They now mark it stale, and it fetches when it is enabled. `Refetch()` on a disabled query still fetches. When several observers share a key, the entry fetches as long as at least one enabled observer is subscribed.
- **Cache entries nobody subscribes to are evicted after `CacheTime`.** Eviction used to start only when an entry's last subscriber left, so an entry created by `PrefetchAsync` — or by `SetArgs` on a query that was never rendered — stayed cached until the client was disposed. Its `CacheTime` countdown now starts when it is created and is cancelled by the first subscriber. Prefetched data is therefore only available for `CacheTime`; raise it if you prefetch far ahead of use.
- **A query outliving its cache entry fetches again instead of throwing.** Reading `CurrentState` or subscribing to `State` on a long-lived query whose entry had been evicted threw `ObjectDisposedException`. It now re-attaches to its current args, exactly as calling `SetArgs` again would.
- **`PrefetchAsync` no longer runs alongside the regular fetch.** A prefetch for a key that is already fetching waits for that fetch instead of starting a second, concurrent one. Cancelling the prefetch token now reliably leaves the entry `Idle` (it could stay `Fetching` for good), and if a subscriber joined in the meantime the entry fetches again for it.
- **A throwing mutation callback no longer disables the mutation.** An exception from `OnMutate`, `OnSuccess`, `OnFailure` or `OnSettled` used to break the mutation permanently: every later `Execute` returned without running the mutator. Now a throwing `OnMutate` fails that run — the mutator is skipped, the state becomes `Failure` with the exception, and `OnFailure` / `OnSettled` fire. An exception from `OnSuccess`, `OnFailure` or `OnSettled` is logged at `Error` level and otherwise ignored; the state the mutation already published stands and `OnSettled` still runs.
- **`FetchNextPage` / `FetchPreviousPage` that have nothing to fetch are true no-ops.** Calling one before the first page had loaded, or when there was no further page, cancelled whatever fetch was in flight and left the query stuck in `Fetching`. They now leave a running fetch alone. An exception thrown by `GetNextPageParam` / `GetPreviousPageParam` during such a call still reaches the caller, but no longer stops the query from responding to later calls.
- **Several `<QueryRefreshMonitor>` instances no longer interfere.** Disposing one of two monitors on a page could remove the other's browser listeners and leave its own attached.

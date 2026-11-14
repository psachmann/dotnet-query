# Fetchling.Core

Internal implementation is `internal sealed`; the public surface is `IQueryClient`, `IQuery`, `IInfiniteQuery`, `IMutation` (+ `TProgress` variant), the `*Options` records and `IQueryCacheInspector`. Public changes need `PublicAPI.Unshipped.txt` updates.

## Shape

- `QueryClient` owns a `QueryCache`. `CreateQuery` / `CreateStreamedQuery` return a `QueryObserver`, `CreateInfiniteQuery` an `InfiniteQueryObserver`, `CreateMutation` a `Mutation`.
- **Observer vs entry.** `QueryObserver` is a key-switching proxy (each `SetArgs` derives a `QueryKey` and switches to the matching cached `Query`), so observers with equal args share one `Query`. `InfiniteQueryObserver` / `InfiniteQuery` follow the same split and the same semantics.
- `QueryCache` holds entries in one `ConcurrentDictionary`, generic over `ICacheEntry`. A key already held by a different entry shape throws `InvalidOperationException`. Eviction is an `Observable.Timer` on the `IScheduler`, started for any entry without subscribers (prefetched ones too) and cancelled when one joins. An observer whose entry was evicted re-attaches on next use (`QueryObserver.Active`).
- **Inspection split.** Consumer-facing: public `IQueryCacheEntry` (read-only live view + `Refetch`/`Invalidate`/`Cancel`; no `Dispose`, no Rx types, doesn't extend `IQuery`). Lifecycle: internal `ICacheEntry` (`StateChanged`/`Subscribed`/`Unsubscribed`), implemented only by `Query` and `InfiniteQuery`. Keep this split for any new query type. Consumers reach the inspector via `client.TryGetInspector(out var inspector)`; no `InternalsVisibleTo`.

## Invariants (breaking these causes bugs)

All time uses the injected `IScheduler` (`QueryClientFactory.Create` defaults to `DefaultScheduler.Instance`; tests inject `TestScheduler`).

**`Query`**
- Fetches run through `Observable.FromAsync(...).Switch()`. **Never call the fetch routine directly**, including from `PrefetchAsync` (which joins a fetch in flight).
- **No user code may throw out of the fetch routine**; it would fault the `Switch()` subscription and silently kill the entry.
- **Fetch ownership:** `_activeFetch` identifies the one fetch owning `_state`; every published state goes through an ownership check under `_syncRoot`, so a superseded fetch (even one ignoring its token) publishes nothing and stamps no freshness.
- **Invalidation never restarts a fetch in flight.** It marks the entry stale and a regular fetch is followed by one more when it settles. With no subscribers it sets `_isStale` and the fetch waits for the first subscriber (stale-while-revalidate).
- `Cancel()` swaps in a fresh `CancellationTokenSource` so cancelling doesn't poison the entry.
- Observers subscribe via `Observe(isEnabled)`: every subscription retains the entry (`_subscriberCount`), but only enabled ones (`_enabledSubscriberCount`) make an invalidation or a stale join fetch.

**Streamed queries** are not a separate entry type. `QueryObserver.MergeOptions(StreamedQueryOptions)` folds the chunk type into an internal `StreamedFetch` on `EffectiveQueryOptions.Stream`; `Query.FetchAsync` streams whenever it is set, publishing chunks as `QueryState.CreateStreaming` (`Success` + `IsStreaming`). Keep it inside `Query` so `<Suspense>`, `<Transition>`, `QueryViewModel` and DevTools keep working.
- `Success` / `Settled` filter out `IsStreaming` states, so they emit once per completed fetch.
- While a stream is in flight, `Invalidate` only marks stale and is *not* followed by a refetch (`Refetch` still restarts). A streamed query ignores the global `RefetchInterval`.
- Each retry attempt restarts the fold from `InitialValue`; failure/cancel carries the shown partial data as `LastData`.

**`InfiniteQuery`**
- `FetchNextPage` / `FetchPreviousPage` resolve their page param *before* sending the command, so a no-op never cancels the run in flight and a throwing delegate surfaces to the caller, not the pipeline.
- A run builds its result from its snapshot and commits `_pages` only in `Settle`, while it still owns the entry. `RefetchAll` runs under a single fetch span.

**Mutations** (`MutationCore<TArgs,TData,TProgress,TState>` holds the engine; `Mutation<…>` variants only supply state records)
- Order: `OnMutate → Mutator → OnSuccess/OnFailure → OnSettled`. A throwing `OnMutate` fails the run; a throwing settle callback is logged and swallowed. Nothing may escape `ExecuteAsync`, or the pipeline dies.
- Every emission goes through one `_gate` lock and only the active run (`_activeRun`) publishes, so a superseded run or stale retry can't overwrite newer state or report late progress.
- **Don't derive one mutation shape by projecting the other's states**: `UiStateBinding` dedups by reference, so a projected stream re-notifies on every replay. Use the four `Create*` factories.
- The `IProgress<T>` given to the mutator is the engine's synchronous one. Never use `System.Progress<T>` (it posts to the `SynchronizationContext` and can report after settling).

## Observability

`QueryInstrumentation` / `QueryTelemetry` use only BCL `ActivitySource` and `Meter` (no OpenTelemetry package).
- Metrics carry a low-cardinality name (`IQueryCacheEntry.Name`: the `Name` option, else the first `QueryKey` part), never the full key, unless `QueryClientOptions.IncludeQueryKeyInMetrics`. Traces and logs always carry the full key; fetch spans also carry `query.name`.
- Fetch spans carry a `FetchTrigger` (`manual`/`invalidate`/`interval`/`stale`/`prefetch`); infinite queries add `direction` and `query.pages`; streamed queries add `query.chunks`.
- Duration histograms record **seconds**; log messages report **milliseconds**.
- Changing names, units or tags is a silent breaking change: add a migration-guide entry.

## Other

`DefaultRetryHandler` is a no-op (single attempt); users supply `IRetryHandler` for real retries.

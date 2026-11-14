# Code Review: `src/`

Review run on 2026-11-14 with `/code-review high src`, against `main` at `8f667d0`.

**Status: all 10 findings are fixed in the working tree** (regression tests in
`tests/DotNetQuery.Core.Tests/FetchRobustnessTests.cs`; finding 10 is covered by the updated
`QueryRefreshMonitorTests`). The line numbers below refer to the reviewed commit, not the fixed code.

10 findings, most severe first. Findings 1–8 were confirmed with probe tests during the review; 9 and 10
were found by reading the code.

| # | Location | Summary |
|---|---|---|
| 1 | `src/DotNetQuery.Core/Internals/Query.cs:387` | Superseded fetch that ignores its token overwrites newer data |
| 2 | `src/DotNetQuery.Core/Internals/InfiniteQuery.cs:334` | No-op `FetchNextPage` cancels the in-flight fetch and leaves the query stuck in `Fetching` |
| 3 | `src/DotNetQuery.Core/Internals/QueryObserver.cs:84` | Observer keeps pointing at a disposed entry after cache eviction |
| 4 | `src/DotNetQuery.Core/Internals/MutationCore.cs:180` | A throwing user callback permanently kills the mutation |
| 5 | `src/DotNetQuery.Core/Internals/InfiniteQuery.cs:337` | A throwing page-param selector permanently kills the infinite query |
| 6 | `src/DotNetQuery.Core/Internals/Query.cs:402` | Cancelled `PrefetchAsync` leaves the entry stuck in `Fetching` |
| 7 | `src/DotNetQuery.Core/Internals/QueryObserver.cs:114` | Entries that never had a subscriber are never evicted |
| 8 | `src/DotNetQuery.Core/Internals/QueryObserver.cs:106` | Invalidation fetches a disabled query |
| 9 | `src/DotNetQuery.Core/Internals/Query.cs:203` | Invalidation restarts an in-flight fetch, so a short `RefetchInterval` never completes |
| 10 | `src/DotNetQuery.Blazor/wwwroot/QueryRefreshMonitor.js:1` | Module-level listener singletons break with two monitors |

## 1. Superseded fetch that ignores its token overwrites newer data

`src/DotNetQuery.Core/Internals/Query.cs:387`

A superseded fetch whose fetcher does not honour the `CancellationToken` still publishes its result and
stamps freshness, overwriting the newer fetch's data (last-to-finish wins instead of latest-request wins).

**Failure scenario:** Confirmed with a probe test: fetch #1 is slow and ignores the token (e.g.
`(id, _) => api.Get(id)`); `Refetch()` starts fetch #2, which completes with "new"; then fetch #1 completes
and the final state is `Success("old")`, marked fresh so later `Invalidate()` calls are skipped. Only the
streamed path guards against this (`linkedToken.ThrowIfCancellationRequested` in the chunk callback).
`InfiniteQuery` has the same gap at `InfiniteQuery.cs:647`, where a superseded `FetchNext` appends its page
to `_pages` after a `RefetchAll` replaced them.

## 2. No-op `FetchNextPage` cancels the in-flight fetch and leaves the query stuck in `Fetching`

`src/DotNetQuery.Core/Internals/InfiniteQuery.cs:334`

A no-op `FetchNextPage` / `FetchPreviousPage` command still goes through `Switch()`, cancelling the
in-flight fetch, then returns early without emitting any state, leaving the query stuck in `Fetching`.

**Failure scenario:** Confirmed with a probe test: `SetArgs` + subscribe starts the initial load (state
`Fetching`); `FetchNextPage()` is called before it completes (e.g. an infinite-scroll sentinel visible on
first render). `Switch` cancels the initial fetch, whose cancellation handler deliberately emits nothing
because the outer token fired; the new command hits `snapshotPages.Count == 0 → return`. After 500 ms the
state is still `Fetching` with 0 pages and the fetcher was called once. Same result when a refetch is in
flight and `HasNextPage` is false (lines 346–349).

## 3. Observer keeps pointing at a disposed entry after cache eviction

`src/DotNetQuery.Core/Internals/QueryObserver.cs:84`

After the cache evicts an entry, the observer's `_activeQuery` still points at the disposed `Query`, so
`CurrentState` throws and re-subscribing to `State` errors with `ObjectDisposedException`.

**Failure scenario:** Confirmed with a probe test: a long-lived query (e.g. owned by a singleton facade,
args set once) is subscribed, unsubscribed, and `CacheTime` elapses. The entry is disposed
(`Query.Dispose` disposes the `_state` `BehaviorSubject`). `query.CurrentState` then throws
`ObjectDisposedException`, and a `<Suspense>` mounting again gets `OnError(ObjectDisposedException)` from
`query.State` instead of a fresh fetch — unless the caller happens to call `SetArgs` again first.
`InfiniteQueryObserver` has the identical pattern.

## 4. A throwing user callback permanently kills the mutation

`src/DotNetQuery.Core/Internals/MutationCore.cs:180`

User callbacks (`OnMutate` at line 113, `OnSuccess` / `OnFailure` / `OnSettled` at 180–188) run outside the
`try` block, so a throwing callback faults the task, errors the `_execute…Switch().Subscribe()` pipeline,
and permanently kills the mutation.

**Failure scenario:** Confirmed with a probe test: `OnSuccess` throws once (e.g. a navigation or `SetData`
call that fails). State is already `Success`; the next `mutation.Execute(2)` returns normally but the
mutator never runs (runs stays 1), with no error surfaced. Every later `Execute` and every `ExecuteCommand`
on a view model silently does nothing for the lifetime of the mutation.

## 5. A throwing page-param selector permanently kills the infinite query

`src/DotNetQuery.Core/Internals/InfiniteQuery.cs:337`

`GetNextPageParam` / `GetPreviousPageParam` are invoked before the `try` block in `ExecuteAsync`, so an
exception from them faults the command pipeline and the infinite query stops responding to every later
command.

**Failure scenario:** Confirmed with a probe test: `GetNextPageParam` throws once during `FetchNextPage()`
(the exception surfaces to the caller). Afterwards `FetchNextPage()` and `Refetch()` both return normally
but the fetcher is never called again and state never changes — the `Switch` subscription is dead, with no
`Failure` state published. Interval and focus invalidations are dead too.

## 6. Cancelled `PrefetchAsync` leaves the entry stuck in `Fetching`

`src/DotNetQuery.Core/Internals/Query.cs:402`

`PrefetchAsync` calls `FetchAsync` directly, outside the `Switch` pipeline, so cancelling the prefetch token
is misread as "superseded by `Switch`" and no terminal state is emitted; the entry stays in `Fetching`.

**Failure scenario:** Confirmed with a probe test: `PrefetchAsync(1, cts.Token)` then `cts.Cancel()` (the
documented hover-prefetch pattern with a token). The task completes, but the cache entry's status remains
`Fetching` with nothing in flight; a later subscriber or DevTools sees a perpetual loading state until
something else invalidates it. Because it bypasses `Switch`, a prefetch on a key that is already fetching
also runs a second concurrent fetch whose state emissions interleave with the first.

## 7. Entries that never had a subscriber are never evicted

`src/DotNetQuery.Core/Internals/QueryObserver.cs:114`

Cache entries created by `PrefetchAsync` (or by `SetArgs` on an observer nobody subscribes to) are never
evicted, because eviction is only scheduled from the entry's `Unsubscribed` signal, which needs a
subscriber to have joined and left.

**Failure scenario:** Confirmed with a probe test: after `PrefetchAsync(1)` with no subsequent subscriber,
the entry is still in the cache after advancing the scheduler 10 hours past a 5-minute `CacheTime`.
Hover-prefetching over a long list (`PrefetchAsync(id)` per row) grows the cache without bound for a
Singleton client, each entry also holding its fetched data and any `RefetchInterval` timer.

## 8. Invalidation fetches a disabled query

`src/DotNetQuery.Core/Internals/QueryObserver.cs:106`

The enabled flag is only consulted in `SetArgs` and on the false→true transition; `Invalidate()`,
`client.Invalidate(...)`, the interval timer and stale-on-subscribe all reach the cache entry directly and
fetch a disabled query.

**Failure scenario:** Confirmed with a probe test: a query with `IsEnabled = false`, `SetArgs(1)` and one
subscriber makes 0 fetches; `client.Invalidate(_ => true)` (exactly what `QueryRefreshMonitor` does on tab
focus) makes 1; `query.Invalidate()` makes 2. This contradicts the `IQuery.SetEnabled` contract ("fetching
is suspended even if the query is invalidated") and fires dependent queries whose args are not ready.
`InfiniteQueryObserver` is the same.

## 9. Invalidation restarts an in-flight fetch, so a short `RefetchInterval` never completes

`src/DotNetQuery.Core/Internals/Query.cs:203`

Invalidating a regular query with an in-flight fetch restarts it via `Switch` (cancelling the running one),
so a `RefetchInterval` shorter than fetch latency means no fetch ever completes.

**Failure scenario:** `RefetchInterval` = 5 s and the endpoint takes 6 s: each tick finds the entry not
fresh with subscribers > 0, pushes `_invalidate`, and `Switch` cancels the fetch that was 5 s in. The query
stays in `Fetching` indefinitely and never publishes data. With the default `StaleTime` of zero, any
focus/reconnect/mutation invalidation also throws away an in-flight fetch. The streamed path already avoids
this (`_streamsInFlight` only marks stale); the non-streamed path and `InfiniteQuery.Invalidate` (line 226,
which also drops an in-flight `FetchNext`) do not.

## 10. Module-level listener singletons break with two monitors

`src/DotNetQuery.Blazor/wwwroot/QueryRefreshMonitor.js:1`

The listeners are stored in module-level singletons, so a second `<QueryRefreshMonitor>` instance
overwrites the first's references and `unregister()` removes the wrong listeners.

**Failure scenario:** Two monitors on one page (e.g. one in a layout and one in a page, or an old instance
whose async `DisposeAsync` has not finished when a new one registers): the second `register()` overwrites
`_visibilityListener` / `_onlineListener`. Disposing either instance removes only the latest listeners, so
the surviving component stops refetching on focus, while the orphaned listener stays attached forever and
calls `invokeMethodAsync` on a disposed `DotNetObjectReference` on every visibility change (unhandled
promise rejections).

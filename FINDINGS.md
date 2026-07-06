# Code Review Findings — DotNetQuery

**Date:** 2026-07-03
**Scope:** Full library source (`src/DotNetQuery.Core`, `src/DotNetQuery.Extensions.DependencyInjection`, `src/DotNetQuery.Blazor`, `src/DotNetQuery.Blazor.DevTools`), verified against the test suite.
**Branch:** `feat/reproducable-build`

## Summary

The architecture is solid — clean public-interface/internal-implementation split, options-record merging, `IScheduler` injection making time fully testable, and BCL-only telemetry. The XML documentation is excellent. However, there are two high-severity runtime bugs, one broken type class (value-type `TData`), and a cache-lifecycle gap where the implementation doesn't do what its own documentation promises.

---

## High severity

### 1. `SetArgs` leaks a fully-wired `Query` on every cache hit — including a live background fetch loop

`QueryObserver.cs:37-40` constructs a candidate `Query` eagerly and passes it to `GetOrCreate`. On a cache hit the candidate is never disposed — unlike `PrefetchAsync` (`QueryObserver.cs:113-116`), which correctly disposes the losing candidate.

Each orphan holds a `CancellationTokenSource` and three subjects, and — critically — when `RefetchInterval` is set, its constructor (`Query.cs:40-45`) has already subscribed an `Observable.Interval` that invokes the real fetcher on every tick. The orphan isn't in the cache, so `QueryCache.Dispose` never reaches it: every `SetArgs` that hits an existing key (the *common* path — re-set args, Blazor parameter updates, two observers sharing a key) spawns an immortal background loop making real network calls.

**Fix:** Mirror the `PrefetchAsync` dispose-on-loss pattern, or better, defer construction with a factory so the candidate is only built on a cache miss.

### 2. `Cancel()` permanently bricks the query

`Query.cs:135` cancels the `readonly` `_cancellationTokenSource` created once at `Query.cs:10` and never replaced. Every subsequent fetch links to it (`Query.cs:156-159`), so after one `Cancel()` every future fetch — `Refetch()`, invalidation, refetch interval — starts with an already-cancelled token and aborts instantly.

Cancel-then-refetch is a routine UI pattern. The test suite only asserts the cancel → Idle transition (`QueryTests.cs:178`), never a fetch after cancel, so this is invisible to CI.

**Fix:** Recreate the CTS after each cancellation (with care around the dispose path, which also uses it).

### 3. Value-type `TData` is broken in two independent places

- `Query.cs:34`: `options.InitialData is { } initial` — for `TData = int`, `InitialData` defaults to `0`, which always matches, so *every* value-typed query starts in `Success(default)` instead of `Idle`. For `TData = ImmutableArray<T>` the fabricated success carries a default (unusable) struct.
- `Transition.razor:21`: `_state.CurrentData ?? _state.LastData` — never null for a value type, so `Transition` renders `Content(default(TData))` in Idle, Fetching, and Failure states, never showing `Loading`/`Failure`.

The library uses `null` as "absent," which only works for reference types — and the test suite never instantiates a value-type `TData`, so nothing catches it.

**Fix:** Either track presence explicitly (a `HasData` flag on `QueryState`, an `InitialData` presence flag on the options) or constrain `TData : class` and document it. Related: a fetcher legitimately returning `null` leaves `Suspense` stuck on `Loading` forever — worth documenting whichever way the decision goes.

---

## Medium severity

### 4. `RefetchInterval` fires with zero subscribers, contradicting its own docs

`QueryClientOptions.cs:22` promises re-fetching "while they have active subscribers," but `Query.cs:43` pushes straight into `_invalidate`, bypassing the subscriber-count and stale-time gates that `Invalidate()` (`Query.cs:115-133`) enforces. A cached interval query keeps hitting the backend forever with nobody listening. The existing test (`QueryTests.cs:375`) only covers the with-subscriber case.

**Fix:** Route the interval through `Invalidate()` — fixes both the gating and the doc mismatch.

### 5. Cache entries are never evicted automatically

Eviction only ever starts in `QueryObserver.Detach()` → `QueryCache.Remove`. Nothing watches `_subscriberCount` reaching zero, and `QueryObserver.Dispose` (`QueryObserver.cs:121-136`) doesn't detach. Yet the docs on `IQuery.CacheTime` and `QueryClientOptions.CacheTime` describe TanStack semantics: "kept in the cache after all subscribers have disposed. Once elapsed, the cache entry is evicted."

For a CSR singleton client, every distinct key accumulates forever unless consumers remember to call `Detach()` manually — combined with finding 4, abandoned interval queries also keep fetching.

**Fix:** Start the eviction timer when the last `State` subscriber leaves. Pending evictions are already cancelled on re-attach (`QueryCache.cs:22-26`), so the hard half is done.

### 6. `QueryCache.Remove` has a lock gap and an overwrite leak

`QueryCache.cs:45-75`:

- **(a)** It reads `_entries` and writes `_pendingRemovals` without taking `_evictionLock`, so a `Detach` racing `GetOrCreate` can schedule eviction *after* a new observer attached to the entry — `CacheTime` later the query is disposed under a live subscriber, its `State` completes, and the UI silently freezes.
- **(b)** `_pendingRemovals[key] = subscription` overwrites an existing pending timer without disposing it, so a double-`Detach` leaves two live timers.

**Fix:** Take `_evictionLock` for the whole method and dispose any overwritten pending subscription.

### 7. Blazor JS interop: listeners never unregistered, `JSDisconnectedException` unhandled

`QueryRefreshMonitor.js` registers `visibilitychange`/`online` listeners with no way to remove them; after `DisposeAsync` they keep invoking a disposed `DotNetObjectReference` — JS console errors, and dead-circuit calls under Blazor Server.

Additionally, both `QueryRefreshMonitor.DisposeAsync` (`QueryRefreshMonitor.razor.cs:54-62`) and `QueryDevTools.DisposeAsync` (`QueryDevTools.razor.cs:218-224`) call `_module.DisposeAsync()` without catching `JSDisconnectedException`, which is routinely thrown during normal circuit teardown in Blazor Server.

**Fix:** Export an unregister function (or return a cleanup handle) from the JS module and call it in `DisposeAsync`; wrap module disposal in a `try/catch (JSDisconnectedException)`.

---

## Minor

- **Disposed-entry races** — `Query.Refetch`/`Invalidate` on an evicted entry throw `ObjectDisposedException` from subject `OnNext` (no `_disposed` guard), and `QueryCache.Invalidate` reads entries without the eviction lock. Reachable from `QueryRefreshMonitor.OnFocus`'s invalidate-everything racing an eviction timer.
- **Inconsistent locking on `_lastSuccessAt`** — written under `_syncRoot` in `SetData` (`Query.cs:95`) but bare in `FetchAsync` (`Query.cs:180`), read bare in `Invalidate`/`PrefetchAsync`. `DateTimeOffset?` is a multi-word struct, so torn reads are theoretically possible; pick one discipline.
- **Always-on inspector overhead** — every state change of every query allocates a full `Snapshot()` list and pushes it through `_entriesSubject` (`QueryCache.cs:33`) even when DevTools isn't attached; `GetOrCreate` also emits to subscribers while holding `_evictionLock`. Consider only materializing snapshots when `Entries` has observers, and emitting outside the lock.
- **Superseded fetch emits a spurious state** — a switched-out fetch's cancellation handler still emits `CreateIdle(lastData)` (`Query.cs:200`), which can land after the replacement fetch's `Fetching` emission. Guard emission on "still the current fetch."
- **`AddDotNetQuery` uses `services.Add`** — repeated calls stack duplicate registrations; `TryAdd` is the idiomatic guard (`ServiceCollectionExtensions.cs:30`).
- **`SerializeData` allocates `JsonSerializerOptions` per call** (`QueryDevTools.razor.cs:209`) — cache a static instance (CA1869; surfaces as a suggestion under current analyzer settings).
- **Key/type collision** — `GetOrCreate`'s cast (`QueryCache.cs:28`) means two queries with different `TData` sharing a key fail with a bare `InvalidCastException`; a descriptive error would save users debugging time.
- **Replay semantics undocumented** — `Success`/`Failure` replay the latest matching state to late subscribers (BehaviorSubject underneath), but the docs say "emits on each successful fetch," which doesn't suggest replay. Same for `Mutation.Success`.
- **`QueryKey.Default = From("\0")`** (`QueryKey.cs:26`) can collide with a user-constructed key; a private sentinel instance compared by reference would be airtight.

---

## Strengths

- The `IScheduler`-everywhere design is the best decision in the codebase — it's why the test suite can assert real timing semantics deterministically.
- The public API is small and coherent; `EffectiveOptions` merging keeps global/per-query precedence in one place.
- `PrefetchAsync`'s dispose-on-loss shows the right ownership instinct (it just needs to be applied consistently).
- XML docs are unusually complete, including honest remarks like the eventual-consistency caveat on `InvalidateKeys`.

## Suggested order of attack

1. **Findings 1 and 2** — small, contained diffs, highest user impact.
2. **Finding 7** — mechanical Blazor hygiene.
3. **Findings 4 + 5 together** — one lifecycle decision (subscriber-gated intervals + last-unsubscribe eviction); fixing them makes the `CacheTime` docs true.
4. **Finding 3** — needs a design decision (`TData : class` constraint vs. explicit presence tracking); decide before v1 since it changes the public contract.
5. **Finding 6 and the minors** — cleanup.

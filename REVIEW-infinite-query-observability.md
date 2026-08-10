# Review: infinite queries & OpenTelemetry observability

Branch: `feat/infinite-query-observability` — reviewed 2026-08-10.

Scope: `InfiniteQuery`, `InfiniteQueryObserver`, `InfiniteQueryState`, `InfiniteQueryOptions`,
`PageParam`, `InfinitePageInfo`, `<InfiniteSuspense>` / `<InfiniteTransition>`,
`QueryInstrumentation`, `QueryTelemetry`, `QueryTelemetryTags`, and the telemetry call sites in
`Query`, `Mutation`, and `QueryCache`.

Overall the implementation is solid: the proxy/cache-entry split mirrors the regular query path
faithfully, `Switch()` + linked-token cancellation semantics are consistent, the cancel-rollback
guard is correct and well-commented, and telemetry start/stop pairs are balanced on every exit
path. The findings below are ordered by severity.

## Resolution (2026-08-10)

All findings were fixed on this branch. Decisions confirmed with the maintainer:

- **F1** — value-type pages are not supported; `where TData : class` added across the infinite
  query surface (interface, options, client, internals, Blazor components), mirroring
  `IQuery<TArgs,TData>` and its documented rationale.
- **F2** — `DataComparer` implemented: unchanged pages keep their previous instance during a full
  refetch, and `Success` suppresses re-emissions via an element-wise page-list comparer.
- **F3** — `QueryCache.Dispose` now calls `RecordCacheDisposed` per remaining entry (decrements
  the gauge without counting an eviction).
- **F4** — `CurrentData` returns an array snapshot.
- **F5/F6** — `ExecuteAsync` resolves page params once up front (replacing `ShouldFetch`); the
  direction handlers report whether a fetch actually ran, so no-op/disposed runs no longer stamp
  freshness or record success, and the `attempts` tag reflects real fetcher invocations.
- **F7** — `InfiniteSuspense` docs corrected.
- **F8** — `InfiniteSuspenseTests` and `InfiniteTransitionTests` added (plus
  `InfiniteQueryObserverTests` for the `Success` dedup behavior).
- **F9** — duration histograms switched to seconds (`"s"` unit, OTel semconv); log messages stay
  in ms. Fetch spans now carry `query.name`, infinite fetch spans carry `query.pages`.

---

## Findings

### F1 — `InitialData` seeds a bogus `default(TData)` page for value-type pages (High, correctness)

`src/DotNetQuery.Core/Internals/InfiniteQuery.cs:59`

```csharp
if (options.InitialData is { } initialData)
```

`IInfiniteQuery<TArgs, TData, TPageParam>` has **no `class` constraint on `TData`** (unlike
`IQuery<TArgs, TData>`, which documents *"Constrained to reference types because the library uses
null to represent 'no data yet'"*). For a struct page type (e.g. `TData = int` or a readonly
record struct), `TData?` on an unconstrained generic is just `TData`, so `is { }` is **always
true** — every such query starts in `Success` state pre-seeded with a `default(TData)` page it
never fetched, and the initial fetch is skipped until invalidation.

**Fix (recommended):** add `where TData : class` to `IInfiniteQuery<,,>`,
`IQueryClient.CreateInfiniteQuery`, `InfiniteQueryOptions<,,>`, `InfiniteQuery<,,>`,
`InfiniteQueryObserver<,,>`, and the two Blazor components (`@typeparam TData where TData : class`),
matching the regular query surface and its documented rationale. Alternative if struct pages must
stay supported: replace `TData? InitialData` with an optional wrapper (the existing
`PageParam<T>`-style pattern) or an explicit `bool` sentinel — but pick one; do not leave the
current silent misbehavior.

### F2 — `DataComparer` is accepted, documented, and never used (Medium, API contract)

`src/DotNetQuery.Core/InfiniteQueryOptions.cs:64` (doc: *"Equality comparer applied per page to
detect structurally identical pages"*), merged at
`src/DotNetQuery.Core/Internals/InfiniteQueryObserver.cs:147`, then never referenced by
`InfiniteQuery` or `InfiniteQueryObserver`.

Compare the regular path: `Query.FetchAsync` uses it to preserve reference identity of unchanged
data (`Query.cs:311`) and `QueryObserver.Success` uses it for `DistinctUntilChanged`
(`QueryObserver.cs:94`). The infinite `Success` observable re-emits identical page lists on every
settled fetch.

**Fix (recommended):** implement it —
1. In `InfiniteQueryObserver.Success`, apply `DistinctUntilChanged` with a derived
   `IReadOnlyList<TData>` comparer that compares element-wise using `_options.DataComparer`.
2. In `InfiniteQuery.ExecuteRefetchAllAsync`, when a re-fetched page equals the old page at the
   same index per `DataComparer`, keep the old instance (render stability, parity with
   `Query.emitData`).

Alternative: delete the option from `InfiniteQueryOptions` until it does something. Do not ship
an option that silently does nothing.

### F3 — `dotnetquery.cache.entries` drifts upward in SSR mode (Medium, observability)

`src/DotNetQuery.Core/Internals/QueryCache.cs:177-222` (`Dispose`) and
`QueryInstrumentation.RecordCacheMiss` / `RecordCacheEviction`.

The `cache.entries` UpDownCounter is incremented on every miss and decremented **only** by timer
eviction. `QueryCache.Dispose` disposes remaining entries without decrementing. The counter lives
on the process-wide static `QueryTelemetry.Meter`, but in `Ssr` mode the client (and its cache) is
**scoped** — every disposed scope leaks its live-entry count into the global gauge permanently, so
the metric climbs forever under SSR traffic.

**Fix:** in `QueryCache.Dispose`, call `_instrumentation.RecordCacheDisposed(...)` (a new method
that decrements `_cacheEntries` without counting an eviction) for each entry still present, or
decrement by `_entries.Count` per metric-name group.

### F4 — `InfiniteQuery.CurrentData` returns a live view of the mutable page list (Medium, thread safety)

`src/DotNetQuery.Core/Internals/InfiniteQuery.cs:98-107`

```csharp
return _pages.Count > 0 ? (object)_pages.AsReadOnly() : null;
```

`AsReadOnly()` is a wrapper over the live `List<T>`. The lock protects only the wrapper creation;
the consumer (DevTools panel) enumerates outside the lock while a fetch mutates `_pages` on
another thread → `InvalidOperationException` ("collection was modified") or torn reads.

**Fix:** return a snapshot: `return _pages.Count > 0 ? (object)(IReadOnlyList<TData>)[.. _pages] : null;`

### F5 — No-op runs are recorded as successful fetches (Low, telemetry accuracy)

`src/DotNetQuery.Core/Internals/InfiniteQuery.cs:271-373`

`ExecuteAsync` starts the span and `RecordFetchStart` after `ShouldFetch`, but the direction
handlers can still early-return without fetching (the `_disposed` checks at
`InfiniteQuery.cs:529/584/654`, and the re-invocation of `GetNextPageParam`/`GetPreviousPageParam`
returning `None` if the user delegate is impure). When that happens the run still:
- stamps `_lastSuccessAt` (marking the query fresh though nothing was fetched),
- records `RecordFetchSuccess` + an `Ok` span with `attempts = 1` and zero fetcher invocations.

Related nit: the cancelled path tags `attempts = counter.Retries + 1` even when the fetcher was
never invoked.

**Fix:** have `Execute*Async` return `bool didFetch`; skip `_lastSuccessAt` and success telemetry
when `false`. Track actual first attempts in `AttemptCounter` (e.g. an `Attempts` field bumped in
`FetchPageAsync`) so the `attempts` tag reflects reality on all exit paths.

### F6 — Page-param delegates invoked repeatedly per operation (Low, robustness/perf)

`ShouldFetch` (`InfiniteQuery.cs:473`), the direction handlers, and `ComputeHasMorePages` each
call `GetNextPageParam`/`GetPreviousPageParam` with the same inputs — up to three calls of a
user-supplied delegate per fetch. Correct for pure delegates, surprising for expensive or impure
ones.

**Fix (folds into F5):** compute the boundary param once in `ExecuteAsync` (this subsumes
`ShouldFetch`) and pass the resolved `PageParam<TPageParam>` into the direction handler.

### F7 — `<InfiniteSuspense>` XML doc contradicts its behavior on failure with cached pages (Low, docs)

`src/DotNetQuery.Blazor/InfiniteSuspense.razor.cs:7-8` claims `Failure` renders *"when the query
fails with no pages to fall back on"*, but the razor template
(`InfiniteSuspense.razor:6-13`) renders `Failure` on **any** failure — `CreateFailure` preserves
pages, yet `IsSuccess` is false so the `Content` branch is skipped. (The behavior itself is
reasonable for strict-suspense semantics; `InfiniteTransition` correctly shows `Content` in that
case.)

**Fix:** correct the XML doc on `InfiniteSuspense` (class remark and the `Failure` parameter doc)
to say failure is always shown, and that `InfiniteTransition` is the stale-while-revalidate
variant.

### F8 — No bUnit tests for `<InfiniteSuspense>` / `<InfiniteTransition>` (Medium, test gap)

`tests/DotNetQuery.Blazor.Tests/` has `SuspenseTests` and `TransitionTests` but no infinite
counterparts. Core-level `InfiniteQueryTests` coverage is otherwise good (32 tests).

**Fix:** add `InfiniteSuspenseTests` and `InfiniteTransitionTests` mirroring the existing suites:
Idle→Loading, first success→Content with pages, failure-without-pages→Failure,
failure-with-pages (Suspense→Failure, Transition→Content), IsFetchingNextPage flag visible in
Content context, re-subscription when the `Query` parameter instance changes.

### F9 — Optional OTel polish (Low)

- Fetch spans carry `query.key` but not `query.name`, while metrics carry only `query.name` —
  add `query.name` to fetch/mutation spans so traces and metrics correlate.
- The infinite `refetch_all` span could tag the number of pages fetched (e.g. `query.pages`).
- Duration histograms are declared in `"ms"`; current OTel semantic conventions favor seconds for
  `*.duration` histograms. Decide **before** the first public release — renaming/re-uniting a
  metric later is a breaking observability change. Keeping ms is defensible; document the choice.

---

## Known accepted behaviors (no action planned)

- Cancel-rollback TOCTOU: `Cancel()` immediately followed by a new command can, in a very narrow
  window, let the cancelled run's rollback emission race the new run's `Fetching` emission. Shares
  the same shape (and guard comment) with `Query.FetchAsync`; considered acceptable.
- Cache entries created via `SetArgs` whose `State` is never subscribed are never scheduled for
  eviction (eviction is driven by `Unsubscribed`). Pre-existing, shared with regular queries.
- A `RefetchAll` counts as one fetch span / one duration sample regardless of page count — this is
  the documented design.

---

## Fix plan

Ordered so each step builds and tests green independently. Run
`dotnet csharpier format .` and `dotnet test --configuration Release` after each step.

1. **F1 — constrain `TData : class` for infinite queries.**
   Touch: `IInfiniteQuery.cs`, `IQueryClient.cs` (`CreateInfiniteQuery`), `InfiniteQueryOptions.cs`,
   `Internals/InfiniteQuery.cs`, `Internals/InfiniteQueryObserver.cs`,
   `Internals/EffectiveInfiniteQueryOptions.cs`, `InfiniteQueryState.cs` (if constrained members
   need it), `InfiniteSuspense.razor`, `InfiniteTransition.razor`. Copy the constraint-rationale
   XML doc from `IQuery<TArgs,TData>`. Verify all existing tests still compile (they use record
   class pages).
2. **F4 — snapshot in `CurrentData`.** One-line change + a concurrency-free unit test asserting
   the returned list does not change after a subsequent fetch mutates pages.
3. **F3 — decrement `cache.entries` on cache dispose.** Add
   `QueryInstrumentation.RecordCacheDisposed(QueryKey, string)`, call per remaining entry in
   `QueryCache.Dispose`. Test with `MetricCollector` in `QueryInstrumentationTests` +
   a `QueryCacheTests` case (create 2 entries, dispose cache, assert gauge back to 0).
4. **F5 + F6 — accurate fetch accounting.** Refactor `ExecuteAsync`: resolve the boundary page
   param once (replaces `ShouldFetch`), pass it to the direction handlers, return `didFetch`,
   count real attempts in `AttemptCounter`, and only stamp `_lastSuccessAt` / record success when
   a fetch actually ran. Tests: disposed-mid-run records no success metric; attempts tag equals
   fetcher invocations on cancel.
5. **F2 — wire up `DataComparer`.** Element-wise list comparer for
   `InfiniteQueryObserver.Success` (`DistinctUntilChanged`), old-instance reuse per unchanged page
   in `ExecuteRefetchAllAsync`. Tests: refetch with identical data does not re-emit `Success`;
   unchanged pages keep reference identity after refetch.
6. **F7 — fix `InfiniteSuspense` XML docs.**
7. **F8 — add `InfiniteSuspenseTests` / `InfiniteTransitionTests`** (cases listed above).
8. **F9 — optional polish** (`query.name` on spans, `query.pages` tag, ms-vs-s decision). Do last;
   each is independent and skippable.

## Open questions — resolved

1. **F1:** value-type pages are not a goal → `class` constraint (parity with `IQuery`).
2. **F2:** implement `DataComparer` → done.
3. **F9:** switch duration histograms to seconds per OTel semantic conventions → done.

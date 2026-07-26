# Observability Improvement Plan

Plan for closing the gaps found in a review of `src/DotNetQuery.Core/Observability/` and every
instrumentation call site (`Query.cs:262-304`, `QueryCache.cs:51/59`, `Mutation.cs:100-138`).

**Target release:** v2.0.0 — Phase 1 changes the metric tag set, which breaks existing dashboards.
It should ship in the same major bump as the `where TData : class` constraint rather than in a
minor release afterwards.

## Current state

What already works and should not be disturbed:

- BCL-only (`ActivitySource` / `Meter` / `ILogger`), no OpenTelemetry dependency in the library.
- One source name (`DotNetQuery`) for traces, metrics, and the log category.
- Spans on both operations (`query.fetch`, `mutation.execute`) with `otel.status_code` / `error.type`.
- `dotnetquery.query.active` is balanced — every exit path out of `FetchAsync` (success, cancel,
  failure) decrements exactly once, and the `_disposed` early-return happens before the increment.

## Verified constraint (affects Phase 3)

.NET 10's `Meter` **deduplicates non-observable instruments** but **not observable ones**. Measured
by reflecting over `Meter._instruments` after 1000 identical `Create*` calls:

| Instrument kind | Retained after 1000 identical creations |
|---|---|
| `CreateHistogram` / `CreateCounter` / `CreateUpDownCounter` | 1 |
| `CreateObservableGauge` | 1000 |

`QueryInstrumentation` is constructed per `IQueryClient`, and `QueryExecutionMode.Ssr` registers
the client as **Scoped** — i.e. once per request. Today that is safe because all five instruments
are non-observable. **Any `ObservableGauge` added to the `QueryInstrumentation` constructor would
leak one instrument per request.** Phase 3 is designed to avoid observable instruments entirely for
this reason.

---

## Phase 1 — Fix metric cardinality (blocking for v2.0.0)

**Problem.** Every instrument except `mutation.duration` is tagged with `key.ToString()`
(`QueryKey.cs:71`), which includes the args — `users:42`. That is one time series per user ID.
OpenTelemetry's default cardinality limit is 2000 per stream; past it everything collapses into an
overflow bucket, and before it you pay per series. Traces tolerate high cardinality, metrics do not.

**Approach.** Split the two concerns: full key on spans, low-cardinality name on metrics.

- [ ] Add `public string? Name { get; init; }` to `QueryOptions<TArgs, TData>` and to
      `EffectiveQueryOptions<TArgs, TData>` (additive, non-breaking at compile time).
- [ ] Resolve the metric dimension once per `Query<TArgs, TData>`, in this order:
      1. `_options.Name` when set
      2. `_key.Parts.Count > 0 ? _key.Parts[0].ToString() : "unknown"`
- [ ] Add `TagQueryName = "query.name"` to `QueryTelemetryTags`.
- [ ] Change `RecordFetch*` / `RecordCache*` signatures to take the resolved name instead of the
      `QueryKey`, and tag metrics with `query.name`.
- [ ] Keep `activity?.SetTag(TagQueryKey, _key.ToString())` on the span unchanged — traces are where
      the full key belongs.
- [ ] Keep the full key in log messages (logs are not a cardinality-limited store).
- [ ] `QueryCache` needs the same name for hit/miss; derive it from the key at the cache layer using
      the same `Parts[0]` fallback (the cache does not see `QueryOptions`).

**Open decision.** Whether to offer `QueryClientOptions.IncludeQueryKeyInMetrics` (default `false`)
as an escape hatch for consumers whose keys are genuinely bounded (e.g. a fixed set of feature
names). Recommendation: **yes, add it** — it is a few lines, and without it there is no way back to
the old behaviour for someone who relied on it.

**Tests.** Assert via a `MeterListener` that a fetch on key `["users", 42]` records
`query.name = "users"` and that no `query.key` tag is present on any measurement; assert the span
still carries the full `users:42`.

---

## Phase 2 — Retries, cancellations, and failure detail

### 2a. Retry telemetry (currently promised but absent)

Three places already claim retry telemetry exists:

- `QueryTelemetry.cs` XML doc — *"fetch durations, cache hits/misses, retry counts, and mutation durations"*
- `QueryClientFactory.cs:22` — *"log messages on fetch, cache, retry, and mutation events"*
- `docs/doc/guides/observability.md` — *"keeps only warnings (failures and retries)"*

There is no retry instrument, no retry log, and `IRetryHandler.ExecuteAsync` has no reporting hook.

**Approach — count attempts by wrapping the fetcher delegate.** This needs **no change to
`IRetryHandler`** and works with any user-supplied handler, because every retry is by definition a
re-invocation of the delegate the handler was given:

```csharp
var attempts = 0;

var data = await _options.RetryHandler.ExecuteAsync(
    ct =>
    {
        var attempt = Interlocked.Increment(ref attempts);

        if (attempt > 1)
        {
            activity?.AddEvent(new ActivityEvent("retry"));
        }

        return _options.Fetcher(_args, ct);
    },
    linkedToken
);
```

- [ ] Apply the wrapper in `Query.FetchAsync` and in `Mutation.ExecuteAsync`.
- [ ] Add `Counter<long> dotnetquery.query.retries` and `dotnetquery.mutation.retries`, incremented
      by `attempts - 1` when `attempts > 1` — on the **failure path too**, not just success.
- [ ] Tag the span with `attempts` (int) on all terminal paths.
- [ ] Add a `logger.LogWarning` on the retry path so the documented "Warning keeps failures and
      retries" claim becomes true.

**Note.** `attempts` is read after the `await` completes, so no memory barrier beyond the
`Interlocked` increment is needed; retries within a handler are sequential.

### 2b. Make cancellations visible

`RecordFetchCancelled` (`QueryInstrumentation.cs:71`) only decrements the active counter — no
duration, no count. In a library where `Switch()` cancels in-flight fetches by design, the
cancellation *rate* is a primary health signal: it says a key is thrashing or is being
over-invalidated.

- [ ] Add `StatusCancelled = "cancelled"` to `QueryTelemetryTags`.
- [ ] Record the duration histogram with `status = cancelled` in `RecordFetchCancelled` /
      `RecordMutationCancelled` (this also yields the count for free — no separate counter needed).
- [ ] Set `activity?.SetStatus(ActivityStatusCode.Error, "cancelled")` on the cancel path in
      `Query.cs:284-297` and `Mutation.cs:119-129`; today those spans end `Unset` and are
      indistinguishable from spans that were never completed.

### 2c. `error.type` on failure metrics

- [ ] Add `error.type` as a tag on the `status = failure` duration records (it is already on the
      span). Exception type names are bounded, so this is cardinality-safe, and it lets you break
      failures down by exception without joining to traces.

### 2d. Give mutations an identity

`RecordMutationStart()` takes no arguments, `mutation.duration` carries only `status`, and the
`mutation.execute` span has no identity tag. An app with a dozen mutations gets one aggregate
number and a log line reading `"Mutation started"`.

- [ ] Add `public string? Name { get; init; }` to `MutationOptions<TArgs, TData>` and
      `EffectiveMutationOptions<TArgs, TData>`, defaulting to `typeof(TArgs).Name`.
- [ ] Add `TagMutationName = "mutation.name"`; tag both the span and `mutation.duration`.
- [ ] Thread the name through all four `RecordMutation*` methods and their log messages.

**Tests.** `MeterListener`-based assertions for each new tag/instrument; a retry-handler stub that
invokes the delegate three times, asserting `retries == 2` on both the success and failure paths.

---

## Phase 3 — Cache metrics

Automatic eviction exists (`QueryCache.Remove` + the `Unsubscribed` wiring) and `IQueryInspector`
already surfaces entry counts and `ObserverCount` to DevTools, but none of it reaches the meter.
Hits and misses alone cannot answer "is the cache growing without bound" — exactly the bug class
that eviction was added to fix.

- [ ] `UpDownCounter<int> dotnetquery.cache.entries` — `+1` where `GetOrCreate` takes the
      cache-miss branch (`QueryCache.cs:51`), `-1` where an entry is actually evicted.
- [ ] `Counter<long> dotnetquery.cache.evictions` — incremented in the eviction timer callback.

**Deliberately not an `ObservableGauge`** — see the verified constraint above. An `UpDownCounter` is
non-observable, so `Meter` deduplicates it and per-request `QueryInstrumentation` construction in
SSR mode stays leak-free. It also avoids needing a static registry of live `QueryCache` instances.

- [ ] Tag both with `query.name` (Phase 1 rules), not `query.key`.
- [ ] Verify the `+1`/`-1` pairing holds across the three eviction paths: timer fire, `Remove`
      followed by re-add (cancelled removal), and `QueryCache.Dispose`. Dispose should **not**
      decrement per entry — the whole meter dimension goes away with the client.

**Tests.** Extend `QueryCacheTests` with a `MeterListener`: assert the entry count returns to zero
after the eviction timer fires, and that a cancelled pending removal does not double-decrement.

---

## Phase 4 — Fetch trigger tag

There is currently no way to tell a manual `Refetch()` from an interval tick from an `Invalidate()`
from a deferred stale-while-revalidate fetch. This is the signal you actually want when a key is
fetching more often than expected, and it is something generic HTTP instrumentation cannot give you.

- [ ] Add an internal `enum FetchTrigger { Manual, Invalidate, Interval, Stale, Prefetch }`.
- [ ] Change `Subject<Unit> _invalidate` to `Subject<FetchTrigger>` in `Query<TArgs, TData>`.
- [ ] Update the pipeline in the constructor:
      ```csharp
      _subscriptions.Add(
          _invalidate.Select(trigger => Observable.FromAsync(ct => FetchAsync(trigger, ct))).Switch().Subscribe()
      );
      ```
- [ ] Map the call sites: `Refetch()` → `Manual`; `Invalidate()` → `Invalidate`;
      `Observable.Interval` → `Interval`; the deferred `becameActive && _isStale` path in the
      `State` observable → `Stale`; `PrefetchAsync` → `Prefetch`.
- [ ] Add `TagTrigger = "trigger"` and tag both the span and the duration histogram. Five bounded
      values, so it is cardinality-safe on metrics.

**Out of scope.** Focus/reconnect refreshes from `<QueryRefreshMonitor>` arrive via
`IQueryClient.Invalidate(predicate)` and are indistinguishable from an ordinary invalidation. Giving
them their own trigger would require plumbing a reason through `IQueryClient.Invalidate`, which is a
public API change — worth considering separately, not here.

---

## Phase 5 — Allocation and logging hygiene

- [ ] **Cache `QueryKey.ToString()`.** It runs `string.Join` over a LINQ `Select` on every call
      (`QueryKey.cs:71`) and is invoked unconditionally by `RecordCacheHit`/`RecordCacheMiss` — that
      is every `GetOrCreate`, whether or not anything is listening. Back it with a lazily-computed
      readonly field. `QueryKey` is immutable, so this is safe.
- [ ] **Convert to `[LoggerMessage]` source-generated logging.** All ten call sites currently use
      `logger.LogDebug`/`LogWarning` with boxed argument arrays and no `IsEnabled` guard. Source
      generation removes both costs.
- [ ] **Consider `Activity` sampling cost.** `StartActivity` returns `null` when nothing is
      listening, so the existing `activity?.` pattern is already correct — no change needed, noted
      only so it is not "fixed" later by mistake.

**Not doing:** switching duration units from `ms` to seconds. OpenTelemetry semantic conventions
prefer seconds, but `ms` is the prevailing .NET convention and the existing dashboards/docs assume
it. Flagging the divergence is enough; changing it is churn for no operational gain.

---

## Phase 6 — Documentation and consistency pass

- [ ] Update `docs/doc/guides/observability.md`: new instruments, the `query.name` vs `query.key`
      split (with an explicit note on *why* metrics do not carry the full key), the `trigger` and
      `attempts` tags, `status = cancelled`, and the new log messages.
- [ ] Fix the XML doc on `QueryTelemetry.Meter` — it currently lists "retry counts", which only
      becomes true after Phase 2a.
- [ ] Fix the XML doc on `QueryClientFactory.Create`'s `logger` parameter (same retry claim).
- [ ] Document `QueryOptions.Name` / `MutationOptions.Name` in the queries and mutations guides,
      framing them as the metric dimension.
- [ ] Add a short "cardinality" section explaining the failure mode for anyone tempted to re-add the
      key via `IncludeQueryKeyInMetrics`.
- [ ] Run `dotnet csharpier .` and the full suite with coverage before opening the PR.

---

## Sequencing

Phases 1 and 2 are the ones that matter: Phase 1 because unbounded cardinality is actively harmful
at scale, Phase 2a because the documentation currently promises telemetry that does not exist.
Phases 3-5 are additive and can land independently.

Phase 1 must land before the v2.0.0 tag — it is additive at the API level but breaking for
dashboards, so it belongs in the major bump. Everything else is non-breaking and could follow in a
2.x minor if the release needs to ship sooner.

| Phase | Breaking? | Must ship in 2.0.0 |
|---|---|---|
| 1 — cardinality | Metric tags only (no compile break) | Yes |
| 2 — retries / cancel / error.type / mutation name | No (additive) | Preferred |
| 3 — cache metrics | No | No |
| 4 — trigger tag | No | No |
| 5 — allocation & logging | No | No |
| 6 — docs | No | Follows whatever ships |

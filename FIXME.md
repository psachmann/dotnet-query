# Known Issues & Architectural Improvements

Issues identified via static analysis and architectural review. Grouped by severity.

---

## Resolved

All issues previously listed in this file have been fixed. A summary is preserved below for historical context.

| # | Issue | Fix location |
|---|-------|-------------|
| 1 | `Query.Dispose()` race — `FetchAsync` called `_state.OnNext()` after disposal | `_disposed` guards added after every `await` yield point in `FetchAsync` |
| 2 | `Mutation.Dispose()` race — `ExecuteAsync` called `_state.OnNext()` after disposal | `_disposed` guards added after every `await` yield point in `ExecuteAsync`; callbacks also guarded |
| 3 | `DefaultRetryHandler` lost original stack trace on re-throw | Replaced `throw lastException!` with `ExceptionDispatchInfo.Capture(lastException!).Throw()` |
| 4 | `IsEnabled` on `IQuery`/`IMutation` was an `IObserver<bool>` with a misleading property-like name | Replaced with `void SetEnabled(bool enabled)` on both interfaces and all implementations |
| 5 | Blazor components re-subscribed on every `OnParametersSet` regardless of whether `Query` changed | Added `_subscribedQuery` field; `OnParametersSet` skips teardown/resubscribe when `ReferenceEquals(Query, _subscribedQuery)` |
| 6 | `QueryKey.From()` accepted `null` elements, producing silent key collisions | `ArgumentNullException` for null array; `ArgumentException` for null elements via `Array.Exists` |
| 7 | `QueryClientOptions` accepted invalid values (negative times, null handler) without error | `Validate()` method added to `QueryClientOptions`; called in `QueryClientFactory.Create()` and `AddDotNetQuery()` |
| 8 | `OnSettled` callback on `Mutation` was called on cancellation but the asymmetry with `Query` was undocumented | XML doc added to `MutationOptions.OnSettled` clarifying it fires on success, failure, **and** cancellation |
| 9 | `Invalidate()` was a silent no-op within `StaleTime` with no explanation | XML doc added to `IQuery.Invalidate()` explaining the `StaleTime` guard and directing consumers to `Refetch()` as the bypass |
| 10 | `IQueryCache` internal-only prevented alternative cache implementations | Documented in notes; not promoted to public (would require a larger API surface change — defer to a future version) |
| 11 | `IMutation<TArgs,TData>` had no awaitable `ExecuteAsync` | Documented; not added (requires deeper contract changes around cancellation semantics — defer to a future version) |
| 12 | `MutationState<TData>.Data` inconsistent with `QueryState<TData>.CurrentData` | `Data` renamed to `CurrentData`; `HasData` kept as `IsSuccess` (correct for value types) |
| 13 | `IQueryCache` was a dead internal interface — never mocked in tests, never a consumer extensibility point | Deleted `IQueryCache.cs`; `QueryClient` and `QueryObserver` now reference `QueryCache` directly |

---

## Deferred (future work)



### B. No awaitable `ExecuteAsync` on `IMutation<TArgs,TData>`
**File:** `src/DotNetQuery.Core/IMutation.cs`

Adding `Task<TData> ExecuteAsync(TArgs args, CancellationToken cancellationToken = default)` requires pinning the cancellation contract (e.g., does cancellation throw or return default?) and deciding whether the existing `Cancel()` method is the right primitive alongside an awaitable API. Defer until the usage pattern is clearer.

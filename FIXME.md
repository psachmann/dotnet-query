# Known Issues & Architectural Improvements

Issues identified via static analysis and architectural review. Grouped by severity.

---

## Critical

### 1. Memory Leak: Unmanaged Subscription in `CreateMutation()`
**File:** `src/DotNetQuery.Core/Internals/QueryClient.cs:19–32`

When `MutationOptions.InvalidateKeys` is non-empty, a subscription to `mutation.Success` is created but never stored or disposed. Every call to `CreateMutation()` with invalidation keys leaks a subscription that holds a closure reference, preventing garbage collection. In long-running applications this accumulates indefinitely.

**Fix:** Store the subscription inside `Mutation<TArgs, TData>` and dispose it as part of the mutation's own `Dispose()`, or return a composite disposable from the constructor.

---

### 2. Race Condition: Unsynchronized `_isStale` in `Query.State`
**File:** `src/DotNetQuery.Core/Internals/Query.cs:48–58`

`_subscriberCount` is incremented with `Interlocked.Increment`, but `_isStale` is a plain `bool` with no synchronization. The increment-then-check-`_isStale` pattern is not atomic. Between the two operations another thread can flip `_isStale`, causing a fetch to be missed or triggered twice. Neither `_subscriberCount` nor `_isStale` is marked `volatile`.

**Fix:** Mark both fields `volatile`, or protect the entire check-and-act block with a `lock`. Consider using `Interlocked.CompareExchange` for the flag.

---

## Major

### 3. Silent Exception Swallowing in Mutation Callbacks
**File:** `src/DotNetQuery.Core/Internals/Mutation.cs:74–75`

`OnSuccess` is invoked inside the `try` block whose `catch` handles `Exception`. If `OnSuccess` throws, the error is caught by the failure handler: the mutation transitions to `Failure` state and `OnFailure` is called — even though the underlying mutator succeeded. The caller observes a failed mutation with no indication that the server call actually worked.

**Fix:** Invoke `OnSuccess`, `OnFailure`, and `OnSettled` outside the try/catch that wraps the mutator call, or wrap them in their own try/catch with explicit error handling (e.g. route to a separate error observable).

---

### 4. `QueryKey` Nullability Contract Violation
**File:** `src/DotNetQuery.Core/IQuery.cs`

The XML documentation states that `Key` returns `null` before args are set for the first time, but the return type is non-nullable `QueryKey`. The implementation returns `QueryKey.Default` (a sentinel constructed from `"\0"`), which is a real value that compares as equal to other `Default` instances. Callers writing defensive null-checks will never hit them; callers who don't may treat the sentinel as a meaningful key.

**Fix:** Either change the return type to `QueryKey?` and return `null` as documented, or remove the null documentation and clearly document `QueryKey.Default` as the initial sentinel state with guidance on how to detect it.

---

### 5. `OnSettled` Not Called on Mutation Cancellation
**File:** `src/DotNetQuery.Core/Internals/Mutation.cs:77–79`

`OnSettled` is invoked in the success and failure paths but is skipped entirely when the mutation is cancelled via `OperationCanceledException`. Callers using `OnSettled` as a "mutation is finished" hook will miss the cancellation case, leading to stuck loading states or unreleased resources.

**Fix:** Call `OnSettled` in the cancellation handler too, or document explicitly that `OnSettled` is only guaranteed to fire on success and failure.

---

## Moderate

### 6. No `IQueryCache` Abstraction
**File:** `src/DotNetQuery.Core/Internals/QueryObserver.cs:4–5`

`QueryObserver<TArgs, TData>` depends directly on the concrete `QueryCache` class. There is no interface, so the cache implementation cannot be swapped, extended, or mocked in isolation. Tests rely on `InternalsVisibleTo` and must construct real `QueryCache` instances.

**Fix:** Extract an `IQueryCache` interface covering `GetOrCreate`, `Remove`, and `Invalidate`. Have `QueryObserver` and `QueryClient` depend on the interface rather than the concrete type.

---

### 7. Cache Eviction Timer Race Condition
**File:** `src/DotNetQuery.Core/Internals/QueryCache.cs:19–38`

When `GetOrCreate` is called after `Remove`, the pending removal timer is cancelled via `TryRemove` + `Dispose`. However, between `TryRemove` succeeding and `GetOrAdd` completing, the timer can fire on another thread and attempt to evict the entry that is being re-added. The remove/re-add sequence is not atomic.

**Fix:** Use a lock or `CancellationToken`-based approach to make the cancel-and-re-add operation atomic, ensuring the timer cannot fire between the two steps.

---

### 8. `ConcurrentDictionary` Iterated During Potential Modification
**File:** `src/DotNetQuery.Core/Internals/QueryCache.cs:48–56`

`Invalidate(Func<QueryKey, bool>)` iterates `_entries` with a `foreach`. While `ConcurrentDictionary` iteration is safe from exceptions, entries added or removed concurrently during iteration may be silently skipped or processed twice.

**Fix:** Snapshot the keys before iterating: `foreach (var key in _entries.Keys.ToList())`.

---

### 9. State Updates After `Query` Disposal
**File:** `src/DotNetQuery.Core/Internals/Query.cs:82–98`

`Dispose()` cancels the `CancellationTokenSource` and then calls `_state.OnCompleted()` and `_state.Dispose()`. A `FetchAsync` already scheduled on the `IScheduler` can be in-flight between token cancellation and subject disposal, and may call `_state.OnNext()` on an already-disposed `BehaviorSubject`, causing an `ObjectDisposedException`.

**Fix:** Add a `_disposed` guard at the top of `FetchAsync` (after any `await` yield points) so that state updates are skipped once disposal has begun.

---

### 10. Inconsistent `RetryHandler` Nullability Between Query and Mutation
**Files:** `src/DotNetQuery.Core/QueryOptions.cs`, `src/DotNetQuery.Core/MutationOptions.cs`

`QueryOptions.RetryHandler` is nullable — callers can opt out of retries by passing `null`, which falls back to the global default in `EffectiveQueryOptions`. `MutationOptions.RetryHandler` is non-nullable and defaults to `new DefaultRetryHandler()`. There is no way to disable retries for a mutation without implementing a custom no-op `IRetryHandler`.

**Fix:** Apply the same nullable + merge pattern to `MutationOptions`, or make both non-nullable with a shared `NoRetryHandler.Instance` constant for opting out.

---

### 11. Manual Subscription Disposal Boilerplate Repeated Across Types
**Files:** `src/DotNetQuery.Core/Internals/Query.cs`, `QueryObserver.cs`, `Mutation.cs`

Every class manually tracks 2–3 `IDisposable` subscription fields and disposes them individually in `Dispose()`. This pattern is repeated and error-prone — a missed field is a silent leak. `System.Reactive.Disposables.CompositeDisposable` already provides this aggregation.

**Fix:** Replace individual subscription fields with a single `CompositeDisposable _subscriptions` and call `_subscriptions.Add(...)` at construction time. `Dispose()` then reduces to `_subscriptions.Dispose()`.

---

## Minor

### 12. Stack Trace Lost in `DefaultRetryHandler`
**File:** `src/DotNetQuery.Core/Internals/DefaultRetryHandler.cs:41`

After exhausting retries, the handler re-throws the last exception with `throw lastException!`, which replaces the original stack trace with the re-throw site. Callers lose the location of the original failure, making debugging significantly harder.

**Fix:** Use `System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lastException!).Throw()` to preserve the original stack trace.

---

### 13. `IsEnabled` Is a Misleading Name for an `IObserver<bool>`
**File:** `src/DotNetQuery.Core/IQuery.cs`

The property `IsEnabled` sounds like a readable boolean. It is actually an `IObserver<bool>` — a push target into which callers push `true`/`false` values. This naming is counterintuitive and likely to confuse API consumers expecting a readable property.

**Fix:** Rename to `EnabledSink`, `EnabledObserver`, or replace the observer with a plain `void SetEnabled(bool enabled)` method if the observable pattern is not needed externally.

---

### 14. Blazor Components Re-Subscribe on Every `OnParametersSet`
**Files:** `src/DotNetQuery.Blazor/Suspense.razor:35–44`, `Transition.razor`

On every parameter change, the component unconditionally disposes the current subscription and creates a new one, even if the `Query` reference has not changed. In components that re-render frequently this causes unnecessary subscription churn.

**Fix:** Track the previous `Query` reference. Skip disposal and re-subscription if the same instance is passed again.

---

### 15. `QueryKey.From()` Accepts `null` Elements Without Validation
**File:** `src/DotNetQuery.Core/QueryKey.cs`

`QueryKey.From(params object[] parts)` accepts `null` elements. `null` parts are serialized as the string `"null"`, which is indistinguishable from a key intentionally containing the string `"null"`. This can produce collisions between intentional and unintentional keys.

**Fix:** Either throw `ArgumentException` for `null` parts, or restrict the parameter type to `params string[]` to enforce non-null values at compile time.

---

### 16. `QueryClientFactory` Performs No Option Validation
**File:** `src/DotNetQuery.Core/QueryClientFactory.cs`

Options such as negative `CacheTime`, negative `StaleTime`, or a `null` global `RetryHandler` are accepted silently. Invalid values produce incorrect runtime behavior (e.g. immediate eviction, NullReferenceException during fetch) far from the construction site.

**Fix:** Add a `Validate()` method on `QueryClientOptions` (or inline validation in the factory) that throws `ArgumentOutOfRangeException` / `ArgumentNullException` with a clear message for invalid values.

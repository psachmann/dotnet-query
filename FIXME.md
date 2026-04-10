# Known Issues & Architectural Improvements

Issues identified via static analysis and architectural review. Grouped by severity.

---

## Major

### 1. State Updates After `Query` Disposal
**File:** `src/DotNetQuery.Core/Internals/Query.cs:82–98`

`Dispose()` cancels the `CancellationTokenSource` and then calls `_state.OnCompleted()` and `_state.Dispose()`. A `FetchAsync` already scheduled on the `IScheduler` can be in-flight between token cancellation and subject disposal, and may call `_state.OnNext()` on an already-disposed `BehaviorSubject`, causing an `ObjectDisposedException`.

**Fix:** Add a `_disposed` guard at the top of `FetchAsync` (after any `await` yield points) so that state updates are skipped once disposal has begun.

---

### 2. State Updates After `Mutation` Disposal
**File:** `src/DotNetQuery.Core/Internals/Mutation.cs:50–67`

The same race condition as issue #1 applies to `Mutation`. `Dispose()` completes internal subjects, but an `ExecuteAsync` call already in-flight may still call `_state.OnNext()` on the disposed `BehaviorSubject`. No `_disposed` guard is present, unlike `Query` which has one partially in place.

**Fix:** Introduce a `_disposed` flag (set at the start of `Dispose()`) and guard all `_state.OnNext()` call sites in `ExecuteAsync` to skip state updates after disposal.

---

## Minor

### 3. Stack Trace Lost in `DefaultRetryHandler`
**File:** `src/DotNetQuery.Core/Internals/DefaultRetryHandler.cs:41`

After exhausting retries, the handler re-throws the last exception with `throw lastException!`, which replaces the original stack trace with the re-throw site. Callers lose the location of the original failure, making debugging significantly harder.

**Fix:** Use `System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lastException!).Throw()` to preserve the original stack trace.

---

### 4. `IsEnabled` Is a Misleading Name for an `IObserver<bool>`
**Files:** `src/DotNetQuery.Core/IQuery.cs`, `src/DotNetQuery.Core/IMutation.cs`

The property `IsEnabled` sounds like a readable boolean. It is actually an `IObserver<bool>` — a push target into which callers push `true`/`false` values. This naming is counterintuitive and likely to confuse API consumers expecting a readable property. The inconsistency is compounded by the fact that `QueryState.IsIdle`, `IsSuccess`, etc. are genuine readable boolean helpers on the same types.

**Fix:** Rename to `EnabledSink`, `EnabledObserver`, or replace the observer with a plain `void SetEnabled(bool enabled)` method if the observable pattern is not needed externally.

---

### 5. Blazor Components Re-Subscribe on Every `OnParametersSet`
**Files:** `src/DotNetQuery.Blazor/Suspense.razor:35–44`, `Transition.razor`

On every parameter change, the component unconditionally disposes the current subscription and creates a new one, even if the `Query` reference has not changed. In components that re-render frequently this causes unnecessary subscription churn.

**Fix:** Track the previous `Query` reference. Skip disposal and re-subscription if the same instance is passed again.

---

### 6. `QueryKey.From()` Accepts `null` Elements Without Validation
**File:** `src/DotNetQuery.Core/QueryKey.cs`

`QueryKey.From(params object[] parts)` accepts `null` elements. `null` parts are serialized as the string `"null"`, which is indistinguishable from a key intentionally containing the string `"null"`. This can produce collisions between intentional and unintentional keys.

**Fix:** Either throw `ArgumentException` for `null` parts, or restrict the parameter type to `params string[]` to enforce non-null values at compile time.

---

### 7. `QueryClientFactory` Performs No Option Validation
**File:** `src/DotNetQuery.Core/QueryClientFactory.cs`

Options such as negative `CacheTime`, negative `StaleTime`, or a `null` global `RetryHandler` are accepted silently. Invalid values produce incorrect runtime behavior (e.g. immediate eviction, NullReferenceException during fetch) far from the construction site.

**Fix:** Add a `Validate()` method on `QueryClientOptions` (or inline validation in the factory) that throws `ArgumentOutOfRangeException` / `ArgumentNullException` with a clear message for invalid values.

---

### 8. `OnSettled` Callback Asymmetry Between `Query` and `Mutation` on Cancellation
**Files:** `src/DotNetQuery.Core/Internals/Query.cs`, `src/DotNetQuery.Core/Internals/Mutation.cs`

`MutationOptions.OnSettled` is invoked on cancellation (the mutation's `ExecuteAsync` exits via the cancelled path and still calls `OnSettled`). The equivalent query-side callback pattern does not call `OnSettled` when a fetch is cancelled. This silent asymmetry is likely to surprise consumers who expect parity between the two.

**Fix:** Decide on and document the canonical contract — either both call `OnSettled` on cancellation or neither does — and update the implementation and tests accordingly. If the intent is that cancellation is not "settled", update `Mutation` to match.

---

### 9. `Invalidate()` Is a Silent No-Op Within `StaleTime`
**File:** `src/DotNetQuery.Core/Internals/Query.cs:69–87`

Calling `Invalidate()` on a query whose last successful fetch occurred within the configured `StaleTime` window returns without marking the query as stale or triggering a refetch. There is no indication to the caller that the invalidation was suppressed. This conflicts with the intuitive expectation that `Invalidate()` always forces a refetch on next access.

**Fix:** Either rename the existing guarded path to `TryInvalidate()` and make `Invalidate()` unconditionally mark the query as stale (bypassing `StaleTime`), or document this behavior prominently on the interface and throw a more specific exception or return a boolean indicating whether invalidation had an effect.

---

### 10. `IQueryCache` Is Internal, Preventing Alternative Implementations
**File:** `src/DotNetQuery.Core/Internals/IQueryCache.cs`

`IQueryCache` is `internal`, and `QueryClient` instantiates `QueryCache` directly rather than injecting it. This prevents consumers from providing alternative caching strategies (e.g. bounded caches, distributed caches) and makes unit testing of cache-dependent code difficult without running the real `QueryCache`.

**Fix:** Promote `IQueryCache` to `public` (or introduce a dedicated public `IQueryStore` abstraction), accept it as a constructor parameter on `QueryClient`, and expose it through the DI registration to allow optional overrides.

---

### 11. No Awaitable API Surface for `Mutation` Results
**File:** `src/DotNetQuery.Core/IMutation.cs`

`Execute(TArgs args)` is fire-and-forget. Consumers who need to await the result must subscribe to the `Success` or `Failure` observables and compose their own `Task`-based wrappers. This is unnecessarily complex for the common case of wanting `await mutation.ExecuteAsync(args)` returning `TData` or throwing on failure.

**Fix:** Add `Task<TData> ExecuteAsync(TArgs args, CancellationToken cancellationToken = default)` to `IMutation<TArgs, TData>` that returns a `Task` completing when the mutation settles, propagating exceptions on failure and throwing `OperationCanceledException` when cancelled.

---

### 12. `QueryState<TData>` and `MutationState<TData>` Have Inconsistent Data Property Names
**Files:** `src/DotNetQuery.Core/QueryState.cs`, `src/DotNetQuery.Core/MutationState.cs`

`QueryState<TData>` exposes `CurrentData` and `LastData`, while `MutationState<TData>` exposes a single `Data` property. The inconsistency makes it harder to write generic utilities that work across both state types, and the names `CurrentData`/`LastData` are more verbose than necessary.

**Fix:** Align naming across both types. A consistent scheme such as `Data` (current) and `PreviousData` (last successful), or a unified interface `IQueryableState<TData>` that both types implement, would reduce cognitive overhead.

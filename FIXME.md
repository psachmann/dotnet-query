# Known Issues & Architectural Improvements

Issues identified via static analysis and architectural review. Grouped by severity.

---

## Major

### 1. State Updates After `Query` Disposal
**File:** `src/DotNetQuery.Core/Internals/Query.cs:82–98`

`Dispose()` cancels the `CancellationTokenSource` and then calls `_state.OnCompleted()` and `_state.Dispose()`. A `FetchAsync` already scheduled on the `IScheduler` can be in-flight between token cancellation and subject disposal, and may call `_state.OnNext()` on an already-disposed `BehaviorSubject`, causing an `ObjectDisposedException`.

**Fix:** Add a `_disposed` guard at the top of `FetchAsync` (after any `await` yield points) so that state updates are skipped once disposal has begun.

---

## Minor

### 2. Stack Trace Lost in `DefaultRetryHandler`
**File:** `src/DotNetQuery.Core/Internals/DefaultRetryHandler.cs:41`

After exhausting retries, the handler re-throws the last exception with `throw lastException!`, which replaces the original stack trace with the re-throw site. Callers lose the location of the original failure, making debugging significantly harder.

**Fix:** Use `System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(lastException!).Throw()` to preserve the original stack trace.

---

### 3. `IsEnabled` Is a Misleading Name for an `IObserver<bool>`
**File:** `src/DotNetQuery.Core/IQuery.cs`

The property `IsEnabled` sounds like a readable boolean. It is actually an `IObserver<bool>` — a push target into which callers push `true`/`false` values. This naming is counterintuitive and likely to confuse API consumers expecting a readable property.

**Fix:** Rename to `EnabledSink`, `EnabledObserver`, or replace the observer with a plain `void SetEnabled(bool enabled)` method if the observable pattern is not needed externally.

---

### 4. Blazor Components Re-Subscribe on Every `OnParametersSet`
**Files:** `src/DotNetQuery.Blazor/Suspense.razor:35–44`, `Transition.razor`

On every parameter change, the component unconditionally disposes the current subscription and creates a new one, even if the `Query` reference has not changed. In components that re-render frequently this causes unnecessary subscription churn.

**Fix:** Track the previous `Query` reference. Skip disposal and re-subscription if the same instance is passed again.

---

### 5. `QueryKey.From()` Accepts `null` Elements Without Validation
**File:** `src/DotNetQuery.Core/QueryKey.cs`

`QueryKey.From(params object[] parts)` accepts `null` elements. `null` parts are serialized as the string `"null"`, which is indistinguishable from a key intentionally containing the string `"null"`. This can produce collisions between intentional and unintentional keys.

**Fix:** Either throw `ArgumentException` for `null` parts, or restrict the parameter type to `params string[]` to enforce non-null values at compile time.

---

### 6. `QueryClientFactory` Performs No Option Validation
**File:** `src/DotNetQuery.Core/QueryClientFactory.cs`

Options such as negative `CacheTime`, negative `StaleTime`, or a `null` global `RetryHandler` are accepted silently. Invalid values produce incorrect runtime behavior (e.g. immediate eviction, NullReferenceException during fetch) far from the construction site.

**Fix:** Add a `Validate()` method on `QueryClientOptions` (or inline validation in the factory) that throws `ArgumentOutOfRangeException` / `ArgumentNullException` with a clear message for invalid values.

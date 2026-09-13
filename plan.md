# Plan: Mvvm package usability

## Why

The Avalonia sample (`samples/DotNetQuery.Samples.Avalonia`) does not reference `DotNetQuery.Mvvm`.
Instead it rebuilds the package's features by hand, and in places gets them slightly wrong:

| Hand-written in the sample | Problem | Replaced by |
|---|---|---|
| `ObservableExtensions.SubscribeOnUiThread` | Posts every emission with no coalescing, and callbacks still run after dispose. | `QueryViewModel` / `ObserveOnUi` |
| `IsLoadingTitle`, `IsLoadingLists`, `CurrentData ?? LastData` | Recomputes `IsLoading` and `DisplayData`. | `QueryViewModel` (already exists) |
| `IsLoadingItems`, `IsLoadingMore`, `HasNextPage`, `LoadMoreCommand` | No view model exists for infinite queries. | `InfiniteQueryViewModel` |
| `AddItemCommand`, `ToggleCommand`, `DeleteCommand` | Each mutation needs its own command, and nothing exposes whether it is running or failed. | `MutationViewModel`, `ToCommand` |
| `Lists.Clear()` + re-add, `_isSyncingLists` | Rebuilding the collection clears the ListBox selection, so a flag is needed to ignore the null it pushes back. | `SyncFrom` |
| `new QueryViewModel<Guid, TodoList>(query)` | Every type argument has to be written out, which gets worse with three of them. | `ToViewModel()` |

**Goal:** a page view model exposes wrapper view models, and XAML binds to them directly
(`{Binding List.DisplayData.Title}`, `{Binding Items.FetchNextPageCommand}`). The only remaining
hand-written code is what is specific to the app.

## Constraints (unchanged from the original Mvvm design)

- The package stays **zero-dependency** beyond `DotNetQuery.Core`. No MVVM toolkit and no platform packages.
- **Mechanics match `QueryViewModel`:**
  - Emissions are coalesced latest-wins through `IUiDispatcher.Post`.
  - The whole state snapshot is swapped before any `PropertyChanged` is raised.
  - `PropertyChanged` is raised only for properties that actually changed.
  - The constructor subscribes and `Dispose()` unsubscribes.
- **Ownership is decided by how the view model is created:**
  - Wrapping an existing query or mutation (the constructor or `ToViewModel()`): the caller owns it.
  - Created from a client plus options: the view model owns it.
- **No base class for page view models.** Consumers already have one; the sample derives from
  CommunityToolkit's `ObservableObject`. Composition only.
- **No DI registration helpers** (`AddDotNetQueryMvvm`) until a concrete composition need appears.
- **Naming avoids toolkit clashes:** `RelayCommand` stays `internal`, and no new public type is named
  `ObservableObject`, `RelayCommand`, or `ObservableCollection*`.
- **All four `src/` build gates stay green:** PublicAPI files, banned symbols (no ambient time),
  trim/AOT analyzers, and package validation.

---

## Phase 0: Core, `IMutation.CurrentState`

`IQuery` and `IInfiniteQuery` expose a synchronous `CurrentState`; `IMutation` does not.

**The reason to add it is double-submit protection.** A mutation cancels its in-flight run when
`Execute` is called again (`Switch()` in `Internals/Mutation.cs`). The view model only learns about
state changes one dispatcher hop later, so a fast double-click can land in that gap:

```
UI thread                               Mutation
─────────                               ────────
click 1 → Execute(args)          ──►    State = Running (queues a UI update)
click 2 → CanExecute?
          VM's copy says Idle    ──►    Execute(args) → cancels run 1, starts run 2
UI update arrives → IsRunning = true
```

`CurrentState` lets `CanExecute` ask the mutation directly. `ExecuteAsync` sets `Running` before its
first `await`, so as long as Rx's `FromAsync` starts the task synchronously, `CurrentState.IsRunning`
is already `true` when the second click is checked. The Phase 3 double-submit test confirms this.
It also sees runs the view model didn't start, which matters because mutations are often shared
(the sample's `TodosMutations` is).

**Initial state is not a reason.** `State` replays the latest value synchronously during `Subscribe`,
so a view model could take its initial state from that replay without `CurrentState`.
`MutationViewModel` still uses `CurrentState` for its initial state, to match `QueryViewModel`.

**The alternative**, if we decide against changing Core, is for the view model to track "running"
itself: set a flag in `Execute` and clear it when a finished state arrives. It misses runs started
elsewhere on a shared mutation. It also needs a run counter rather than a bool, because a stale
`Success` from the previous run, still waiting for its UI update, would otherwise clear the flag
early.

- Add `MutationState<TData> CurrentState { get; }` to `IMutation<TArgs, TData>`, with XML docs matching
  `IQuery.CurrentState`.
- Implement it in `Internals/Mutation.cs` as `_state.Value`.
- Add the entry to `src/DotNetQuery.Core/PublicAPI.Unshipped.txt`.
- Test in `MutationStateTests`: the value is `Idle` initially, `Running` synchronously after `Execute`,
  and `Success`/`Failure` after the mutation completes.
- **Package validation:** adding a member to a public interface is a breaking change for anyone who
  implements the interface (CP0006). The only implementation is internal and we are still in beta, so
  generate a suppression file (`dotnet pack -c Release -p:GenerateCompatibilitySuppressionFile=true`)
  rather than using a default interface member. See open question 1.

## Phase 1: Shared state-dispatch core (internal refactor)

`QueryViewModel` currently contains the coalescing logic (`OnStateEmitted` / `DrainPendingState` /
`ApplyState`). Two more view models would copy it three times. A public class cannot derive from an
internal base class, so extract the logic as a helper that the view models hold instead:

```csharp
internal sealed class UiStateBinding<TState> : IDisposable where TState : class
{
    public UiStateBinding(IObservable<TState> source, TState initial, IUiDispatcher dispatcher,
                          Action<TState, TState> apply);   // apply(previous, next), always on the UI thread
    public TState Current { get; }
}
```

- `QueryViewModel` is refactored onto it with **no behaviour change**. All existing
  `QueryViewModel*Tests` must pass unmodified; that is the acceptance test for this phase.
- **New, on all three view models: `public event EventHandler? StateChanged`.** It is raised on the UI
  thread after all `PropertyChanged` notifications for an applied state. This is the hook for work
  specific to the page (syncing a collection, restoring a selection) without a second raw subscription.
  It replaces the `SubscribeOnUiThread(ApplyState)` pattern.

## Phase 2: `InfiniteQueryViewModel<TArgs, TData, TPageParam>`

```csharp
public class InfiniteQueryViewModel<TArgs, TData, TPageParam> : BindableBase, IDisposable
    where TData : class
{
    public InfiniteQueryViewModel(IInfiniteQuery<TArgs, TData, TPageParam> query, IUiDispatcher? dispatcher = null);
    public InfiniteQueryViewModel(IQueryClient client, InfiniteQueryOptions<TArgs, TData, TPageParam> options,
                                  IUiDispatcher? dispatcher = null);

    public IInfiniteQuery<TArgs, TData, TPageParam> Query { get; }
    public InfiniteQueryState<TData, TPageParam> CurrentState { get; }

    public QueryStatus Status { get; }
    public IReadOnlyList<TData> Pages { get; }
    public IReadOnlyList<TPageParam> PageParams { get; }
    public Exception? Error { get; }
    public bool IsIdle / IsFetching / IsSuccess / IsFailure / HasData / HasError { get; }
    public bool HasNextPage / HasPreviousPage { get; }
    public bool IsFetchingNextPage / IsFetchingPreviousPage { get; }
    public bool IsLoading { get; }   // IsFetching && !HasData (first load only)

    public ICommand RefetchCommand { get; }            // disabled while IsFetching
    public ICommand CancelCommand { get; }             // enabled while any fetch is in flight
    public ICommand FetchNextPageCommand { get; }      // HasNextPage && !IsFetchingNextPage && !IsFetching
    public ICommand FetchPreviousPageCommand { get; }  // mirror of the above

    public void SetArgs(TArgs args);
    public void SetEnabled(bool enabled);
    public event EventHandler? StateChanged;
    protected virtual void Dispose(bool disposing);
}
```

- **Pages are not flattened.** `TData` is not necessarily enumerable. Flattening belongs in the page
  view model, via `StateChanged` + `SyncFrom` (Phase 5).
- **`PropertyChanged` for `Pages`** is compared by reference, the same way `Data` is today.
- **"Any fetch in flight"** is `IsFetching || IsFetchingNextPage || IsFetchingPreviousPage`, because
  `CreateFetchingNext` reports `Status == Success`. The command `CanExecute` checks must account for
  this. Add a private helper and test it explicitly.
- **Tests** (`InfiniteQueryViewModelStateTests`, `…CommandTests`, `…LifecycleTests`,
  `…IntegrationTests`) follow the existing `QueryViewModel` test layout, using a mocked
  `IInfiniteQuery` plus `RecordingSynchronizationContext`.

## Phase 3: `MutationViewModel<TArgs, TData>` and `ToCommand`

```csharp
public class MutationViewModel<TArgs, TData> : BindableBase, IDisposable
{
    public MutationViewModel(IMutation<TArgs, TData> mutation, IUiDispatcher? dispatcher = null);
    public MutationViewModel(IQueryClient client, MutationOptions<TArgs, TData> options, IUiDispatcher? dispatcher = null);

    public IMutation<TArgs, TData> Mutation { get; }
    public MutationState<TData> CurrentState { get; }
    public MutationStatus Status { get; }
    public TData? Data { get; }
    public Exception? Error { get; }
    public bool IsIdle / IsRunning / IsSuccess / IsFailure / HasData / HasError { get; }

    public ICommand ExecuteCommand { get; }   // parameter is TArgs; disabled while running
    public ICommand CancelCommand { get; }    // enabled while running

    public void Execute(TArgs args);
    public ICommand ToCommand<TParam>(Func<TParam, TArgs> map, Func<TParam, bool>? canExecute = null);
    public ICommand ToCommand(Func<TArgs> args, Func<bool>? canExecute = null);   // args come from the page VM

    public event EventHandler? StateChanged;
    protected virtual void Dispose(bool disposing);
}
```

- **Double-submit protection** (why this matters: Phase 0). `CanExecute` reads
  `Mutation.CurrentState.IsRunning` as well as the applied state. `Execute` raises `CanExecuteChanged`
  immediately on the calling (UI) thread, so the button disables before a second click is processed.
  Tests:
  - Two back-to-back `ExecuteCommand.Execute` calls with no dispatcher drain run the mutation once.
  - `CanExecute` is `false` right after `Execute`, before any drain.
  - A run started directly on the shared `IMutation` disables the command.
- **The command parameter is cast strictly.** `ExecuteCommand.Execute(object?)` casts to `TArgs`. A
  mismatch throws `ArgumentException`, and the message names both the expected and actual types.
  Nothing is converted: XAML string-to-int coercion is the binding's job. Casting is trim/AOT safe.
- **`ToCommand` commands** re-evaluate `CanExecute` on every state apply. The `Func<bool>` overload
  also needs a public `RaiseCanExecuteChanged` path so page view models can refresh it when their own
  inputs change (e.g. `NewItemDescription`). Decide between returning a small public `IQueryCommand`
  interface or reusing `ICommand` plus a VM-level `RefreshCommands()`. See open question 3.
- **Tests:** `MutationViewModelStateTests`, `…CommandTests` (double-submit, parameter-cast error,
  `ToCommand` mapping), `…LifecycleTests`, `…IntegrationTests`.

## Phase 4: `ToViewModel()` and `ObserveOnUi`

New public static class `DotNetQuery.Mvvm.QueryViewModelExtensions`:

```csharp
public static QueryViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
    this IQuery<TArgs, TData> query, IUiDispatcher? dispatcher = null) where TData : class;

public static InfiniteQueryViewModel<TArgs, TData, TPageParam> ToViewModel<TArgs, TData, TPageParam>(
    this IInfiniteQuery<TArgs, TData, TPageParam> query, IUiDispatcher? dispatcher = null) where TData : class;

public static MutationViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
    this IMutation<TArgs, TData> mutation, IUiDispatcher? dispatcher = null);

public static IObservable<T> ObserveOnUi<T>(this IObservable<T> source, IUiDispatcher? dispatcher = null);
```

- **`ToViewModel()` wraps**, so the caller keeps ownership. The XML docs must say so, because the name
  alone does not.
- **`ObserveOnUi` does not coalesce.** It is for *event* streams (`Success`, `Failure`, `Settled`),
  where dropping an emission is a bug. It posts one callback per emission, and a posted callback that
  runs after the subscription is disposed does nothing. Implemented with `Observable.Create`
  (System.Reactive is already transitive through Core).
- **The dispatcher is captured when `ObserveOnUi` is called**, not when the observable is subscribed.
  Document this.
- **Naming:** it is `ObserveOnUi`, not `SubscribeOnUi`, because Rx's `SubscribeOn` means something
  different.
- **Overload ambiguity:** `RS0026` is already suppressed project-wide for the optional-dispatcher
  overloads. Confirm the three `ToViewModel` overloads resolve without ambiguity, including when a type
  implements more than one of the interfaces (none do today).

## Phase 5: `ObservableCollection<T>.SyncFrom`

New public static class `DotNetQuery.Mvvm.ObservableCollectionExtensions`:

```csharp
// Same element type: key identifies the item; itemComparer decides whether it needs replacing.
public static void SyncFrom<T, TKey>(this ObservableCollection<T> target, IEnumerable<T> source,
    Func<T, TKey> keySelector, IEqualityComparer<T>? itemComparer = null) where TKey : notnull;

// Projection: rows are view models built from models (TodoItemViewModel from TodoItem).
public static void SyncFrom<TSource, T, TKey>(this ObservableCollection<T> target, IEnumerable<TSource> source,
    Func<TSource, TKey> sourceKey, Func<T, TKey> targetKey,
    Func<TSource, T> create, Action<T, TSource>? update = null) where TKey : notnull;
```

- **Algorithm:**
  1. Remove targets whose key is no longer present.
  2. Walk the source in order: `Move` existing items to their index, `Insert` new ones, and `Replace`
     (or `update`) changed ones.
  3. If nothing changed, raise nothing.

  The worst case is O(n²) moves, which is acceptable for UI-sized lists. Document the cost.
- **Duplicate source keys throw `ArgumentException`.**
- **Tests** use a recorder on `CollectionChanged`:
  - An unchanged source produces zero events.
  - A single insert, removal, or reorder produces the minimal events.
  - The selected instance survives a refetch that returns equal data.
- **Risk:** some frameworks drop the selection on `Replace`. Verify in the Avalonia sample (Phase 6).
  If the selection is lost, make the projection overload prefer `update` over `Replace`.

## Phase 6: Rewrite the Avalonia sample on top of the package

- Add a `ProjectReference` to `src/DotNetQuery.Mvvm`.
- Delete `ViewModels/ObservableExtensions.cs`.
- **`TodoDetailsViewModel`:**
  - Expose `List` (`QueryViewModel`), `Items` (`InfiniteQueryViewModel`), and `AddItem`
    (`MutationViewModel`).
  - Keep `ObservableCollection<TodoItemViewModel>`, synced through `Items.StateChanged` + `SyncFrom`.
  - Remove the `IsLoading*`, `HasNextPage`, `ErrorMessage`, and `LoadMoreCommand` plumbing.
- **`MainViewModel`:**
  - The sidebar binds to a `QueryViewModel` for lists.
  - `SyncFrom` replaces `Clear()` + re-add, and `_isSyncingLists` is deleted.
  - The `CreateTodoList.Success` handler uses `ObserveOnUi`.
- **`TodoItemViewModel`:** the toggle and delete commands come from `ToCommand`, so the back-reference
  to the owner view model may go away.
- **XAML** binds through the wrapper paths. Compiled bindings (`AvaloniaUseCompiledBindingsByDefault`)
  must still resolve with generic `x:DataType` paths.
- **Dispatcher:** view models are resolved in `OnFrameworkInitializationCompleted` on the UI thread, so
  `SynchronizationContext` capture works without an explicit dispatcher. Keep it that way, and add a
  comment saying why.
- **Check by running the app:**
  - The sidebar selection survives **Refresh**.
  - A double-clicked **Add** creates one item.
  - **Load more** disables while loading.
  - An error message shows when the fetcher throws.

## Phase 7: Docs

- **`docs/doc/guides/mvvm.md`:**
  - Add sections for `InfiniteQueryViewModel`, `MutationViewModel` (including the double-submit
    note), `ToViewModel` + ownership, `ObserveOnUi`, `SyncFrom`, and `StateChanged`.
  - Change the Quick Start to show the `ToViewModel()` pattern next to the client + options constructor.
- **`CLAUDE.md`:** update the "Mvvm layer" section with the new types, the shared internal
  `UiStateBinding`, and the "event streams don't coalesce" rule.
- **`docs/llms.txt`:** update it if it lists the Mvvm API surface.

---

## Verification (every phase)

```bash
dotnet build --configuration Release                          # PublicAPI, banned symbols, AOT analyzers
dotnet test --configuration Release
dotnet csharpier check .
dotnet pack -c Release                                        # package validation (Phase 0 suppression)
```

## Suggested PR split

1. Phases 0 and 1: Core `CurrentState` + the internal refactor. No new Mvvm surface; this is easy to review.
2. Phases 2 and 3: the two new view models.
3. Phases 4 and 5: the helpers.
4. Phases 6 and 7: the sample and docs. This PR is the real-world check of the three above, so expect
   small API changes to come back from it.

## Open questions

1. **How to handle the `IMutation.CurrentState` break.** Options:
   - A suppression file (recommended, since we are in beta).
   - A default interface member that throws `NotSupportedException`.
   - Leave `IMutation` alone and have `MutationViewModel` track "running" itself, with a run counter.
     This misses runs started elsewhere on a shared mutation (see Phase 0).
2. **Should `ToViewModel()` accept an `IQueryClient` + options overload?** For example,
   `client.CreateQueryViewModel(options)`. This is convenient but duplicates the constructors. The
   current lean is no.
3. **How page view models refresh `ToCommand(Func<TArgs>)`.** Either a small public command interface
   with `RaiseCanExecuteChanged()`, or a view-model-level `RefreshCommands()`. The current lean is the
   interface, because CommunityToolkit users already expect `IRelayCommand.NotifyCanExecuteChanged()`.
4. **Should the view models expose a `Reset()` that returns a finished mutation to `Idle`?** Core has
   no reset, so this would be a VM-only state override. The current lean is to leave it out.

## Out of scope

- A base class for page view models, source generators, and DI registration helpers.
- Per-platform satellite packages (`DotNetQuery.Mvvm.Maui`, etc.). The docs' dispatcher snippets
  remain the guidance.
- Changing how `QueryViewModel` behaves today, beyond adding `StateChanged`.

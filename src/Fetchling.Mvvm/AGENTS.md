# Fetchling.Mvvm

Four view models (`QueryViewModel`, `InfiniteQueryViewModel`, `MutationViewModel<TArgs,TData>`, `MutationViewModel<TArgs,TData,TProgress>`) wrap the matching Core type and expose bindable `INotifyPropertyChanged` state. Zero dependencies beyond `Fetchling.Core`. Shared mechanics are internal helpers, not copied per view model:

- `UiStateBinding<TState>` owns the subscription, latest-wins coalescing (a burst becomes one dispatcher `Post`) and swap-before-raise sequencing: `Current` is updated *before* `apply(previous, next)` runs, so a handler reading a sibling property never sees a torn state. Each view model forwards `ApplyState` into it.
- `MutationCommands<TArgs>` owns `ExecuteCommand`/`CancelCommand`, the `ToCommand` overloads, the double-submit guard and the `CanExecuteChanged` refresh list; `ApplyState` calls `RefreshRunningDependentCommands()` when `IsRunning` flips.

## Rules

- **Ownership follows the constructor.** Wrapping an existing query/mutation (or `ToViewModel()`) leaves ownership with the caller; `Dispose()` releases only the subscription. The `IQueryClient` + options constructor owns what it creates and disposes it. The live subscription is what retains the cache entry, so a forgotten dispose pins it.
- **Event streams (`Success`/`Failure`/`Settled`) must not use the coalescing path.** Use `ObserveOnUi()` (one dispatcher post per emission, nothing dropped; the dispatcher is captured when `ObserveOnUi` is *called*). It's not named `SubscribeOnUi` because Rx's `SubscribeOn` means something else.
- `IUiDispatcher.Post` is the marshalling point. Default `SynchronizationContextUiDispatcher` captures `SynchronizationContext.Current` at construction; a null context invokes inline (tests/console).
- **Double-submit guard** reads the wrapped `IMutation.CurrentState` directly, not the dispatcher-marshaled one, closing the one-hop gap after a click and also disabling the command when a run starts on a shared `IMutation`.
- `ToCommand<TParam>` (parameter maps to `TArgs`) and `ToCommand(Func<TArgs>, …)` return `IMutationCommand` (`ICommand` + `RaiseCanExecuteChanged()`). **Call once per command, typically in a constructor**: each call appends to a refresh list with no removal, so calling it per list row leaks. `ExecuteCommand.Execute(object?)` casts strictly to `TArgs` and throws `ArgumentException` naming both types; it never coerces.
- `ToViewModel()` (four overloads) always wraps and never takes ownership. `CompositeDisposable? disposeWith` comes *before* `dispatcher` on purpose, and is a parameter rather than a `DisposeWith()` extension so it can't clash with ReactiveUI's.
- `StateChanged` is raised on the UI thread after every `PropertyChanged` for an applied state. Use it for once-per-state work such as `SyncFrom`.
- `ObservableCollectionExtensions.SyncFrom` reconciles in place (`Move`/`Insert`/`Remove`, `Replace` only in the same-type overload) instead of `Clear()` + re-add, which would lose a bound control's selection. The projection overload's `update` refreshes a matched row instead of replacing it (see `TodoDetailsViewModel` / `TodoItemViewModel` in the Avalonia sample). O(n²) worst case, fine for UI-sized lists. Duplicate keys in `source` throw `ArgumentException`.
- **Naming avoids CommunityToolkit clashes:** `BindableBase` (not `ObservableObject`), internal `RelayCommand`, and `RaiseCanExecuteChanged()` (not `NotifyCanExecuteChanged()`, which `[NotifyCanExecuteChangedFor]` codegen would silently target).
- `RS0026` is suppressed in this project's `.csproj` on purpose (`QueryViewModel` has two constructor overloads with an optional dispatcher).

# MVVM View Models

DotNet Query ships an MVVM binding layer for XAML-based UI frameworks — MAUI, WPF, WinUI, UNO Platform, and Avalonia. Three view models wrap the corresponding Core type and expose its state as bindable `INotifyPropertyChanged` properties, marshaling every change notification onto the UI thread:

| View model | Wraps | Typical use |
|---|---|---|
| `QueryViewModel<TArgs, TData>` | `IQuery<TArgs, TData>` | A single value: a user profile, a settings blob. |
| `InfiniteQueryViewModel<TArgs, TData, TPageParam>` | `IInfiniteQuery<TArgs, TData, TPageParam>` | A paginated list, loaded with "Load more". |
| `MutationViewModel<TArgs, TData>` | `IMutation<TArgs, TData>` | A write operation, bound to a button. |

## Installation

```bash
dotnet add package DotNetQuery.Mvvm
```

The package depends only on `DotNetQuery.Core` — no MVVM toolkit and no platform frameworks.

## Quick Start

Register the query client as usual (a desktop or mobile app uses the default `Csr` execution mode — singleton):

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(1);
});
```

There are two ways to get a view model. Inject `IQueryClient` and construct one directly — the view model creates and owns the query:

```csharp
public sealed class UserPageViewModel : IDisposable
{
    public UserPageViewModel(IQueryClient queryClient, IUserApi api)
    {
        User = new QueryViewModel<int, UserDto>(
            queryClient,
            new QueryOptions<int, UserDto>
            {
                KeyFactory = id => QueryKey.From("users", id),
                Fetcher = (id, ct) => api.GetUserAsync(id, ct),
                StaleTime = TimeSpan.FromMinutes(5),
            });
    }

    public QueryViewModel<int, UserDto> User { get; }

    public void Load(int userId) => User.SetArgs(userId);

    public void Dispose() => User.Dispose();
}
```

Or, if a shared service already owns a long-lived query (the [service/facade pattern](infinite-queries.md#servicefacade-pattern)), wrap it with `ToViewModel()` instead — the view model does **not** own it:

```csharp
public sealed class UserPageViewModel : IDisposable
{
    public UserPageViewModel(UserService users)
    {
        User = users.UserQuery.ToViewModel();
    }

    public QueryViewModel<int, UserDto> User { get; }

    public void Load(int userId) => User.SetArgs(userId);

    public void Dispose() => User.Dispose(); // releases the subscription; UserService still owns UserQuery
}
```

See [Ownership](#ownership) below for the rule that decides which constructor form to reach for.

Bind to it from XAML:

```xml
<ActivityIndicator IsRunning="{Binding User.IsLoading}" />

<Label Text="{Binding User.DisplayData.Name}" />

<Label Text="{Binding User.Error.Message}"
       IsVisible="{Binding User.IsFailure}" />

<Button Text="Refresh" Command="{Binding User.RefetchCommand}" />
```

## QueryViewModel

All properties are read-only snapshots of the wrapped query's state; they update together atomically, and `PropertyChanged` is raised only for properties that actually changed.

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `TData?` | Data from the most recent successful fetch; `null` while fetching. |
| `LastData` | `TData?` | Data from the previous successful fetch, carried across fetches and failures. |
| `DisplayData` | `TData?` | `Data`, falling back to `LastData` during re-fetches — the stale-while-revalidate binding target, equivalent to the Blazor `<Transition>` component. |
| `Error` | `Exception?` | The exception from the most recent failed fetch. |
| `Status` | `QueryStatus` | The raw lifecycle status (`Idle`, `Fetching`, `Success`, `Failure`). |
| `IsLoading` | `bool` | `true` only during the **first** load — fetching with no data to show. Bind a full-page spinner to this. |
| `IsFetching` | `bool` | `true` while **any** fetch is in flight, including background refetches. Bind a subtle refresh indicator to this. |
| `IsIdle` / `IsSuccess` / `IsFailure` | `bool` | Status flags. |
| `HasData` / `HasError` | `bool` | Null checks on `Data` / `Error`. |
| `CurrentState` | `QueryState<TData>` | The whole state record, for converters that need everything. |
| `RefetchCommand` | `ICommand` | Calls `Refetch()`. Disabled while a fetch is in flight. |
| `CancelCommand` | `ICommand` | Calls `Cancel()`. Enabled only while a fetch is in flight. |
| `Query` | `IQuery<TArgs, TData>` | The wrapped query — escape hatch for `Invalidate()`, `PrefetchAsync()`, `Select()`, or direct Rx composition. |

`SetArgs`, `SetEnabled`, and `SetData` are available directly on the view model and delegate to the wrapped query.

## InfiniteQueryViewModel

Wraps an `IInfiniteQuery<TArgs, TData, TPageParam>` — see [Infinite Queries](infinite-queries.md) for the underlying pagination model. `TData` here is a **single page**; pages are not flattened into one list, because `TData` isn't necessarily enumerable (it could be a record with a page of items plus a total count, for instance).

```csharp
public sealed class PostsPageViewModel : IDisposable
{
    public PostsPageViewModel(PostsService posts)
    {
        Posts = posts.PostsQuery.ToViewModel();
    }

    public InfiniteQueryViewModel<Guid, List<PostDto>, int> Posts { get; }

    public void Load(Guid boardId) => Posts.SetArgs(boardId);

    public void Dispose() => Posts.Dispose();
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Pages` | `IReadOnlyList<TData>` | All loaded pages, oldest first. |
| `PageParams` | `IReadOnlyList<TPageParam>` | `PageParams[i]` was used to fetch `Pages[i]`. |
| `Error` | `Exception?` | The exception from the most recent failed fetch. |
| `Status` | `QueryStatus` | The raw lifecycle status. |
| `IsLoading` | `bool` | `true` only during the first load — fetching with no pages yet. |
| `IsFetching` | `bool` | `true` while a **full refetch** (`RefetchAll`) is in flight. Does *not* cover page-navigation fetches — see below. |
| `IsFetchingNextPage` / `IsFetchingPreviousPage` | `bool` | `true` while that specific navigation fetch is in flight. `Status` stays `Success` during these — existing pages remain visible. |
| `HasNextPage` / `HasPreviousPage` | `bool` | Whether `FetchNextPageCommand` / `FetchPreviousPageCommand` can add another page. |
| `IsIdle` / `IsSuccess` / `IsFailure` | `bool` | Status flags. |
| `HasData` / `HasError` | `bool` | `Pages.Count > 0` / `Error is not null`. |
| `CurrentState` | `InfiniteQueryState<TData, TPageParam>` | The whole state record. |
| `RefetchCommand` | `ICommand` | Calls `Refetch()` (re-fetches every loaded page). Disabled while *any* fetch is in flight. |
| `CancelCommand` | `ICommand` | Calls `Cancel()`. Enabled while any fetch is in flight. |
| `FetchNextPageCommand` / `FetchPreviousPageCommand` | `ICommand` | Calls `FetchNextPage()` / `FetchPreviousPage()`. Disabled when there's no next/previous page, or any fetch is already in flight. |
| `Query` | `IInfiniteQuery<TArgs, TData, TPageParam>` | The wrapped query — escape hatch for `Detach()`, `Invalidate()`, or direct Rx composition. |

`SetArgs` and `SetEnabled` delegate to the wrapped query.

```xml
<ActivityIndicator IsRunning="{Binding Posts.IsLoading}" />

<CollectionView ItemsSource="{Binding Posts.Pages}" />
<!-- flatten Pages in your own view model if TData is itself enumerable, e.g. List<PostDto> -->

<Button Text="Load more"
        Command="{Binding Posts.FetchNextPageCommand}"
        IsVisible="{Binding Posts.HasNextPage}" />
```

## MutationViewModel

Wraps an `IMutation<TArgs, TData>` — see [Mutations](mutations.md) for the underlying state machine.

```csharp
public sealed class UserPageViewModel : IDisposable
{
    public UserPageViewModel(IQueryClient queryClient, IUserApi api)
    {
        UpdateName = new MutationViewModel<UpdateNameRequest, UserDto>(
            queryClient,
            new MutationOptions<UpdateNameRequest, UserDto>
            {
                Mutator = (req, ct) => api.UpdateNameAsync(req, ct),
                InvalidateKeys = [QueryKey.From("users")],
            });
    }

    public MutationViewModel<UpdateNameRequest, UserDto> UpdateName { get; }

    public void Dispose() => UpdateName.Dispose();
}
```

| Property | Type | Description |
|----------|------|-------------|
| `Data` | `TData?` | The data returned by the most recent successful execution. |
| `Error` | `Exception?` | The exception from the most recent failed execution. |
| `Status` | `MutationStatus` | The raw lifecycle status (`Idle`, `Running`, `Success`, `Failure`). |
| `IsIdle` / `IsRunning` / `IsSuccess` / `IsFailure` | `bool` | Status flags. |
| `HasData` / `HasError` | `bool` | Null checks on `Data` / `Error`. |
| `CurrentState` | `MutationState<TData>` | The whole state record. |
| `ExecuteCommand` | `ICommand` | Parameter is `TArgs`, cast strictly — see below. Disabled while a run is in flight. |
| `CancelCommand` | `ICommand` | Calls `Cancel()`. Enabled only while a run is in flight. |
| `Mutation` | `IMutation<TArgs, TData>` | The wrapped mutation — escape hatch for `SetEnabled()`, or direct Rx composition via `Success`/`Failure`/`Settled` (see [ObserveOnUi](#observeonui)). |

`ExecuteCommand.Execute(object?)` casts the command parameter to `TArgs` strictly — nothing is converted; XAML's string-to-number coercion is the binding's job, not the command's. A mismatched type throws `ArgumentException` naming both the expected and actual type. `Execute(TArgs args)` is also available directly on the view model for code-behind callers that already have a typed value.

### Double-submit protection

A fast double-click (or a double-tap on mobile) can call `Execute` twice before the first click's `CanExecute = false` has reached the UI thread. `ExecuteCommand` guards against this itself — it reads the wrapped mutation's synchronous `CurrentState` (not the view model's own dispatcher-marshaled state) before starting a run, so the second call is a no-op even before any `PropertyChanged`/`CanExecuteChanged` notification has been processed. The same guard also disables the command when a run was started directly on a shared `IMutation` by something other than this view model.

### ToCommand

`AddItemCommand`, `ToggleCommand`, `DeleteCommand`... every mutation-backed button in a real app needs its own `ICommand`, and each one needs to expose whether it's running or failed. `ToCommand` builds one from the `MutationViewModel` without hand-rolling a command class:

```csharp
// The command parameter maps to TArgs. Use for commands bound per-row in a list,
// with CommandParameter carrying the row's data.
public ICommand ToggleCommand { get; } =
    ToggleItem.ToCommand<TodoItem>(item => new ToggleArgs(item.Id));

// Args come from the page view model instead of the command parameter.
// Use for a single button whose input is a bound field elsewhere on the page.
public ICommand AddItemCommand { get; } =
    AddItem.ToCommand(() => new AddItemArgs(NewItemText), canExecute: () => NewItemText.Length > 0);
```

Both overloads share `ExecuteCommand`'s double-submit protection and return an `IMutationCommand` (still an `ICommand` — bind it exactly like any other), with one addition: `RaiseCanExecuteChanged()`. Call it when an input the `canExecute` predicate reads changes outside the mutation's own state — e.g. from a partial property-changed method:

```csharp
partial void OnNewItemTextChanged(string value) => AddItemCommand.RaiseCanExecuteChanged();
```

**Call `ToCommand` once per command**, typically in the constructor — not per row in a list. Each call appends to the view model's internal "refresh these on every state change" bookkeeping, with no way to remove an entry later; calling it in a loop leaks. For a command that needs a different argument per row, build the command **once** on the page view model with the mapped overload above, and hand the same `ICommand` instance to every row — see the [Avalonia sample](https://github.com/psachmann/dotnet-query/tree/main/samples/DotNetQuery.Samples.Avalonia)'s `TodoDetailsViewModel`/`TodoItemViewModel` for the full pattern, including how the row exposes it in a `CommandParameter` binding.

## StateChanged

All three view models raise a plain `event EventHandler? StateChanged`, on the UI thread, after every `PropertyChanged` notification for an applied state. Use it instead of a second raw subscription to the wrapped query/mutation's `State` for page-specific work that needs to run once per applied state change — most commonly, syncing an `ObservableCollection` with [`SyncFrom`](#syncfrom):

```csharp
public InfiniteQueryViewModel<Guid, List<TodoItem>, int> Items { get; }

public ObservableCollection<TodoItemViewModel> ItemRows { get; } = [];

public TodoDetailsViewModel(IInfiniteQuery<Guid, List<TodoItem>, int> query)
{
    Items = query.ToViewModel();
    Items.StateChanged += (_, _) => SyncItemRows();
}

private void SyncItemRows() =>
    ItemRows.SyncFrom(
        Items.Pages.SelectMany(page => page),
        model => model.Id,
        row => row.Model.Id,
        model => new TodoItemViewModel(model),
        (row, model) => row.Update(model));
```

## Ownership

Every view model has two constructors, with a simple rule:

```csharp
// 1. The view model CREATES the query/mutation and owns it: Dispose() disposes it too.
//    Recommended for page view models receiving IQueryClient via DI.
new QueryViewModel<TArgs, TData>(queryClient, options);
new InfiniteQueryViewModel<TArgs, TData, TPageParam>(queryClient, options);
new MutationViewModel<TArgs, TData>(queryClient, options);

// 2. The view model WRAPS an existing query/mutation and does not own it:
//    Dispose() releases only the view model's subscription; you dispose the original.
//    Use when a shared service owns long-lived queries and mutations.
new QueryViewModel<TArgs, TData>(existingQuery);
new InfiniteQueryViewModel<TArgs, TData, TPageParam>(existingQuery);
new MutationViewModel<TArgs, TData>(existingMutation);
```

Because `CreateQuery`/`CreateInfiniteQuery`/`CreateMutation` are cheap (queries dedupe by key against a shared cache entry; mutations are lightweight either way), creating one per view model is fine — but the [service/facade pattern](infinite-queries.md#servicefacade-pattern) is common enough that wrapping an existing instance needs to be just as easy. `ToViewModel()` is sugar for constructor form 2:

```csharp
public static QueryViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
    this IQuery<TArgs, TData> query, IUiDispatcher? dispatcher = null);

public static InfiniteQueryViewModel<TArgs, TData, TPageParam> ToViewModel<TArgs, TData, TPageParam>(
    this IInfiniteQuery<TArgs, TData, TPageParam> query, IUiDispatcher? dispatcher = null);

public static MutationViewModel<TArgs, TData> ToViewModel<TArgs, TData>(
    this IMutation<TArgs, TData> mutation, IUiDispatcher? dispatcher = null);
```

`ToViewModel()` always **wraps** — the caller keeps ownership, exactly like constructor form 2. The name alone doesn't say so, which is why it's worth stating plainly: disposing a view model built with `ToViewModel()` never disposes the query or mutation it wraps.

## ObserveOnUi

`StateChanged` and the bindable properties above all coalesce: a burst of state emissions collapses into a single UI-thread hop applying only the newest state, because a binding only ever needs the *current* value. That's the wrong behavior for an **event** stream, where every emission matters and dropping one is a bug — `Success`, `Failure`, and `Settled` on both queries and mutations:

```csharp
public static IObservable<T> ObserveOnUi<T>(this IObservable<T> source, IUiDispatcher? dispatcher = null);
```

`ObserveOnUi` posts one callback per emission — nothing is coalesced or dropped:

```csharp
public MainViewModel(TodosMutations mutations)
{
    // Remember the newly created list's id so the refreshed sidebar can select it.
    // Every Success emission must be observed -- missing one would leave the wrong list selected.
    mutations.CreateTodoList.Success
        .ObserveOnUi()
        .Subscribe(list => _pendingSelectionId = list.Id);
}
```

Two things worth knowing:

- **The dispatcher is captured when `ObserveOnUi` is called, not when the returned observable is subscribed.** Call it on the UI thread — or pass a dispatcher explicitly — regardless of where the subscription eventually happens.
- **Naming:** it's `ObserveOnUi`, not `SubscribeOnUi` — Rx's own `SubscribeOn` means something unrelated (which thread runs `Subscribe` itself), and reusing that name here would be misleading.

A callback already posted when the subscription is disposed, but not yet run, becomes a no-op instead of calling into the observer — so disposal is still safe even with events in flight.

## UI Thread Marshaling

Query and mutation state changes originate on background threads. Every view model marshals its `PropertyChanged`, `StateChanged`, and `CanExecuteChanged` notifications onto the UI thread through an `IUiDispatcher`:

```csharp
public interface IUiDispatcher
{
    void Post(Action action);
}
```

By default, the view model captures `SynchronizationContext.Current` at construction. **Construct view models on the UI thread** and this just works on MAUI, WPF, WinUI, UNO, and Avalonia. When no context is present (unit tests, console apps), notifications are invoked inline.

If your view models are constructed off the UI thread — or you prefer explicit platform wiring — supply a dispatcher:

```csharp
// MAUI
public sealed class MainThreadUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
}

// WinUI / UNO (capture the queue on the UI thread)
public sealed class DispatcherQueueUiDispatcher(DispatcherQueue queue) : IUiDispatcher
{
    public void Post(Action action) => queue.TryEnqueue(() => action());
}

// WPF
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action) => dispatcher.BeginInvoke(action);
}

// Avalonia
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
```

Register it once and pass it through:

```csharp
services.AddSingleton<IUiDispatcher, MainThreadUiDispatcher>();

// in the view model:
new QueryViewModel<int, UserDto>(queryClient, options, dispatcher);
```

This dispatcher is what `ObserveOnUi` (above) also uses when one isn't passed explicitly.

Rapid state bursts are coalesced: only the newest state is applied per UI-thread hop, so bindings never churn through intermediate states.

## SyncFrom

Rebuilding an `ObservableCollection` from scratch on every fetch — `Clear()` then re-add — drops whatever a bound control tracks by position, most visibly the selected row in a list box. `SyncFrom` reconciles a collection **in place** instead, computing the minimal `Move`/`Insert`/`Remove` (and, same-type overload only, `Replace`) operations needed to match a new source sequence:

```csharp
// Same element type: keySelector identifies an item's identity across syncs, itemComparer
// (default: EqualityComparer<T>.Default) decides whether a matched item needs replacing.
public static void SyncFrom<T, TKey>(
    this ObservableCollection<T> target, IEnumerable<T> source,
    Func<T, TKey> keySelector, IEqualityComparer<T>? itemComparer = null);

// Projection: rows are view models built from models (TodoItemViewModel from TodoItem).
public static void SyncFrom<TSource, T, TKey>(
    this ObservableCollection<T> target, IEnumerable<TSource> source,
    Func<TSource, TKey> sourceKey, Func<T, TKey> targetKey,
    Func<TSource, T> create, Action<T, TSource>? update = null);
```

```csharp
// Sidebar list: TodoList is a plain record, so the same-type overload is enough.
Lists.SyncFrom(freshLists, list => list.Id);

// Row view models: the projection overload builds TodoItemViewModel from TodoItem,
// and refreshes an existing row in place via `update` rather than replacing it.
ItemRows.SyncFrom(
    freshItems, model => model.Id, row => row.Model.Id,
    model => new TodoItemViewModel(model),
    (row, model) => row.Update(model));
```

A few things worth knowing:

- **The projection overload never replaces a matched row** — only `create` ever produces a new instance, and only for a genuinely new key. A matched row is refreshed via `update` (call every sync, regardless of whether the source actually changed — write it through property setters that themselves no-op when the value is unchanged, the way a typical `BindableBase`-derived row does) or, if `update` is omitted, left untouched entirely. This is what makes a bound selection survive a refetch: the row object a control has selected is never swapped out from under it.
- **The same-type overload can still `Replace`,** because a plain `T` (e.g. a record) has no way to be updated in place — when `itemComparer` reports a matched item changed, the whole value is swapped. If nothing changed, nothing is raised either way.
- **Duplicate keys in `source` throw `ArgumentException`** — checked while materializing, before any collection mutation.
- **Worst case is O(n²) moves** — acceptable for UI-sized lists (tens to low hundreds of rows), not for large ones.

## Lifecycle

**Dispose the view model when its page is torn down.** The view model holds a live subscription to the query's or mutation's state for its entire lifetime — for queries, that subscription is what retains the cache entry and triggers deferred stale fetches. Disposing releases the subscription (starting the cache-time eviction clock, for queries) and, for view models that created their own query/mutation, disposes it too. A forgotten dispose keeps a query's cache entry alive indefinitely.

For pages that are temporarily hidden rather than destroyed, pause fetching instead of disposing (queries and infinite queries only — mutations have no equivalent, since they don't fetch on their own):

```csharp
protected override void OnAppearing() => ViewModel.User.SetEnabled(true);
protected override void OnDisappearing() => ViewModel.User.SetEnabled(false);
```

While disabled, invalidations are deferred; re-enabling re-evaluates the active key and fetches if anything is pending.

## Tips

- **Bind `DisplayData`, not `Data`, for smooth refreshes.** `Data` is `null` while a fetch is in flight; `DisplayData` keeps showing the previous data (stale-while-revalidate).
- **`IsLoading` vs `IsFetching`.** `IsLoading` = first load only (full-page spinner); `IsFetching` = any fetch (subtle refresh indicator, pull-to-refresh spinner). `InfiniteQueryViewModel` additionally separates out `IsFetchingNextPage`/`IsFetchingPreviousPage`, since paging in more data shouldn't trigger the same "everything is reloading" UI as a full refetch.
- **Derive your own view models.** All three view models are unsealed and `BindableBase` is public — derive a page view model that adds its own bindable properties, and override `Dispose(bool)` to clean up additional state.
- **Optimistic updates** work through `SetData(...)` on `QueryViewModel`, exactly as with the raw query API — see the [optimistic updates guide](optimistic-updates.md).

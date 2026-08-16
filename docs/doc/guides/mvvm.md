# MVVM View Models

DotNet Query ships an MVVM binding layer for XAML-based UI frameworks — MAUI, WPF, WinUI, and UNO Platform. `QueryViewModel<TArgs, TData>` wraps an `IQuery<TArgs, TData>` and exposes its state as bindable `INotifyPropertyChanged` properties, marshaling every change notification onto the UI thread.

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

Inject `IQueryClient` into your page view model and create a `QueryViewModel`:

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

Bind to it from XAML:

```xml
<ActivityIndicator IsRunning="{Binding User.IsLoading}" />

<Label Text="{Binding User.DisplayData.Name}" />

<Label Text="{Binding User.Error.Message}"
       IsVisible="{Binding User.IsFailure}" />

<Button Text="Refresh" Command="{Binding User.RefetchCommand}" />
```

## Bindable Properties

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

## Constructors and Ownership

There are two constructors with a simple ownership rule:

```csharp
// 1. The view model CREATES the query and owns it: Dispose() disposes the query.
//    Recommended for page view models receiving IQueryClient via DI.
new QueryViewModel<TArgs, TData>(queryClient, options);

// 2. The view model WRAPS an existing query and does not own it:
//    Dispose() releases only the view model's subscription; you dispose the query.
//    Use when a shared service owns long-lived queries.
new QueryViewModel<TArgs, TData>(existingQuery);
```

Because `CreateQuery` returns a lightweight observer over a shared cache entry (deduplicated by query key), creating one query per view model is cheap — two view models with the same key share a single fetch and cache entry.

## UI Thread Marshaling

Query state changes originate on background threads. The view model marshals all `PropertyChanged` and `CanExecuteChanged` notifications onto the UI thread through an `IUiDispatcher`:

```csharp
public interface IUiDispatcher
{
    void Post(Action action);
}
```

By default, the view model captures `SynchronizationContext.Current` at construction. **Construct view models on the UI thread** and this just works on MAUI, WPF, WinUI, and UNO. When no context is present (unit tests, console apps), notifications are invoked inline.

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
```

Register it once and pass it through:

```csharp
services.AddSingleton<IUiDispatcher, MainThreadUiDispatcher>();

// in the view model:
new QueryViewModel<int, UserDto>(queryClient, options, dispatcher);
```

Rapid state bursts are coalesced: only the newest state is applied per UI-thread hop, so bindings never churn through intermediate states.

## Lifecycle

**Dispose the view model when its page is torn down.** The view model holds a live subscription to the query's state for its entire lifetime — that subscription is what keeps the cache entry retained and triggers deferred stale fetches. Disposing releases the subscription (starting the cache-time eviction clock) and, for client-created queries, disposes the query too. A forgotten dispose keeps the cache entry alive indefinitely.

For pages that are temporarily hidden rather than destroyed, pause fetching instead of disposing:

```csharp
protected override void OnAppearing() => ViewModel.User.SetEnabled(true);
protected override void OnDisappearing() => ViewModel.User.SetEnabled(false);
```

While disabled, invalidations are deferred; re-enabling re-evaluates the active key and fetches if anything is pending.

## Tips

- **Bind `DisplayData`, not `Data`, for smooth refreshes.** `Data` is `null` while a fetch is in flight; `DisplayData` keeps showing the previous data (stale-while-revalidate).
- **`IsLoading` vs `IsFetching`.** `IsLoading` = first load only (full-page spinner); `IsFetching` = any fetch (subtle refresh indicator, pull-to-refresh spinner).
- **Derive your own view models.** `QueryViewModel` is unsealed and `BindableBase` is public — derive a page view model that adds its own bindable properties, and override `Dispose(bool)` to clean up additional state.
- **Optimistic updates** work through `SetData(...)`, exactly as with the raw query API — see the [optimistic updates guide](optimistic-updates.md).

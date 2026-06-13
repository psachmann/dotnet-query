# List for new features

## Core Library Gaps (vs TanStack Query)

- [x] **Infinite/Paginated Queries** — `IInfiniteQuery` equivalent with fetch-next-page / fetch-previous-page support
  - [ ] Initial data

## Blazor-Specific Gaps

- [ ] **Streaming / Real-Time Updates** — Integrate with SSE or SignalR to push live data into the query cache

## .NET Ecosystem Integration

- [ ] **MVVM Integration (`DotNetQuery.Mvvm`)** — `QueryViewModel<TArgs, TData>` wrapping `IQuery<TArgs, TData>` for MVVM-based UI frameworks (MAUI, WPF, UNO Platform); implements `INotifyPropertyChanged` and exposes bindable properties (`IsLoading`, `IsSuccess`, `IsFailure`, `Data`, `Error`); thread marshaling handled per-platform (`MainThread` / `Dispatcher` / `DispatcherQueue`)

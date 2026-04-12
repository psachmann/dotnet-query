# List for new features

## Core Library Gaps (vs TanStack Query)

- **Infinite/Paginated Queries** — `IInfiniteQuery` equivalent with fetch-next-page / fetch-previous-page support
- **Optimistic Updates** — Mutations that temporarily update the cache before server confirmation, with automatic rollback on failure
- **Query Prefetching** — `PrefetchQuery()` on `IQueryClient` to pre-populate the cache before a component mounts

## Blazor-Specific Gaps

- **Network Reconnect Refetching** — Automatically refetch when network connectivity is restored
- **Window Focus Refetching** — Automatically refetch stale queries when the browser tab/window regains focus (Blazor WASM)
- **`<QueryBoundary>` Component** — Error boundary that catches and renders errors for a subtree of query components
- **`<QueryProvider>` Component** — Scope a specific `IQueryClient` instance to a Blazor component subtree
- **Streaming / Real-Time Updates** — Integrate with SSE or SignalR to push live data into the query cache

## .NET Ecosystem Integration

- **MVVM Integration (`DotNetQuery.Mvvm`)** — `QueryViewModel<TArgs, TData>` wrapping `IQuery<TArgs, TData>` for MVVM-based UI frameworks (MAUI, WPF, UNO Platform); implements `INotifyPropertyChanged` and exposes bindable properties (`IsLoading`, `IsSuccess`, `IsFailure`, `Data`, `Error`); thread marshaling handled per-platform (`MainThread` / `Dispatcher` / `DispatcherQueue`)

## Developer Experience

- **DevTools Component** — Blazor component that renders a live view of the query cache (keys, status, data, staleness)
- **Cache Inspector API** — Expose a cache snapshot via `IQueryClient` for custom tooling and debugging

## NuGet Publishing (optional / post-beta)

- Multi-target (`net9.0;net10.0`) for broader compatibility

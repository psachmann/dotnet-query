# List for new features

- Sample Project?
- API Documentation
- ...

## Core Library Gaps (vs TanStack Query)

- **Infinite/Paginated Queries** — `IInfiniteQuery` equivalent with fetch-next-page / fetch-previous-page support
- **Optimistic Updates** — Mutations that temporarily update the cache before server confirmation, with automatic rollback on failure
- **Query Prefetching** — `PrefetchQuery()` on `IQueryClient` to pre-populate the cache before a component mounts
- **Initial Data / Placeholder Data** — Seed the cache with known data to avoid a loading state on first render
- **Data Selectors** — Transform or select a subset of query data without triggering a re-fetch
- **Window Focus Refetching** — Automatically refetch stale queries when the browser tab/window regains focus (Blazor WASM)
- **Network Reconnect Refetching** — Automatically refetch when network connectivity is restored
- **Query Result Structural Equality** — Skip downstream state updates when freshly fetched data is deeply equal to the cached value

## Blazor-Specific Gaps

- **`<QueryBoundary>` Component** — Error boundary that catches and renders errors for a subtree of query components
- **`<QueryProvider>` Component** — Scope a specific `IQueryClient` instance to a Blazor component subtree
- **Streaming / Real-Time Updates** — Integrate with SSE or SignalR to push live data into the query cache

## .NET Ecosystem Integration

- **Observability (ILogger + ActivitySource + Meter)** — OTel-compatible observability using only BCL APIs; no direct OpenTelemetry package dependency in the library itself:
  - **`ILogger<QueryClient>`** (injected via DI, optional) — structured log messages for fetch start/success/failure, cache hit/miss, retry attempts
  - **`System.Diagnostics.ActivitySource`** (static singleton) — distributed trace spans for query fetches and mutation executions, tagged with key and status; automatically picked up by OpenTelemetry instrumentation on the consumer side
  - **`System.Diagnostics.Metrics.Meter`** (static singleton) — metrics for cache hits/misses, fetch duration (histogram), retry count, and active query gauge; automatically picked up by OpenTelemetry instrumentation on the consumer side
- **MVVM Integration (`DotNetQuery.Mvvm`)** — `QueryViewModel<TArgs, TData>` wrapping `IQuery<TArgs, TData>` for MVVM-based UI frameworks (MAUI, WPF, UNO Platform); implements `INotifyPropertyChanged` and exposes bindable properties (`IsLoading`, `IsSuccess`, `IsFailure`, `Data`, `Error`); thread marshaling handled per-platform (`MainThread` / `Dispatcher` / `DispatcherQueue`)

## Developer Experience

- **DevTools Component** — Blazor component that renders a live view of the query cache (keys, status, data, staleness)
- **Cache Inspector API** — Expose a cache snapshot via `IQueryClient` for custom tooling and debugging

## NuGet Publishing (optional / post-beta)

- Multi-target (`net9.0;net10.0`) for broader compatibility

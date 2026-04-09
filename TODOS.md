# List for new features

- Open Telemetry integration
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

- **ILogger Integration** — Log query lifecycle events (fetch start, success, error, retry, cache hit/miss) via `Microsoft.Extensions.Logging`
- **Resilience / Polly Integration** — Optional `DotNetQuery.Extensions.Resilience` package wrapping retry and circuit-breaker with Polly pipelines
- **HttpClient Helpers** — Convenience extension methods for wrapping typed `HttpClient` calls in `QueryOptions`
- **Background Service Queries** — Support for running queries inside `IHostedService` / `BackgroundService` (non-Blazor scenarios)

## Developer Experience

- **DevTools Component** — Blazor component that renders a live view of the query cache (keys, status, data, staleness)
- **Cache Inspector API** — Expose a cache snapshot via `IQueryClient` for custom tooling and debugging

## NuGet Publishing (optional / post-beta)

- Add a package icon (`icon.png`, 128×128) and reference it via `<PackageIcon>` in `Directory.Build.props`
- Add `<PackageReleaseNotes>` or a link to a CHANGELOG in `Directory.Build.props`
- Multi-target (`net9.0;net10.0`) for broader compatibility

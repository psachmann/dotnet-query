# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DotNet Query is a [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor. It provides automatic caching, background refetching, stale-while-revalidate semantics, and reactive state — built on [Rx.NET](https://github.com/dotnet/reactive) observables.

## Commands

```bash
# Restore local tools (CSharpier formatter, etc.)
dotnet tool restore

# Build
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release

# Run a single test project
dotnet test tests/DotNetQuery.Core.Tests --configuration Release

# Run a specific test by name (TUnit)
dotnet test tests/DotNetQuery.Core.Tests --configuration Release -- --filter "FullyQualifiedName~QueryCacheTests"

# Check formatting
dotnet csharpier check .

# Fix formatting (the `format` subcommand is required — bare `dotnet csharpier .` just prints help)
dotnet csharpier format .
```

The formatter (`csharpier`) runs as a CI gate — always run it before committing. Indentation is 4 spaces for C#/Razor/JS files, 2 spaces for XML/config files (see `.editorconfig`).

## Architecture

The solution has four projects under `src/` and three test projects under `tests/`:

| Project | Purpose |
|---|---|
| `DotNetQuery.Core` | Core library: `IQueryClient`, `IQuery<TArgs,TData>`, `IInfiniteQuery<TArgs,TData,TPageParam>`, `IMutation<TArgs,TData>`, cache, observability |
| `DotNetQuery.Extensions.DependencyInjection` | `AddDotNetQuery()` extension; lifetime is Singleton (CSR) or Scoped (SSR) |
| `DotNetQuery.Blazor` | `<Suspense>`, `<Transition>`, `<InfiniteSuspense>`, `<InfiniteTransition>`, `<QueryRefreshMonitor>` Blazor components |
| `DotNetQuery.Blazor.DevTools` | `<QueryDevTools>` live cache inspector component |

### Core layer (`DotNetQuery.Core`)

**Public surface** — `IQueryClient`, `IQuery<TArgs,TData>`, `IInfiniteQuery<TArgs,TData,TPageParam>`, `IMutation<TArgs,TData>`, all `*Options` records.

**Internal implementation** (all `internal sealed`):

- `QueryClient` — owns a `QueryCache`; `CreateQuery` returns a `QueryObserver`; `CreateInfiniteQuery` returns an `InfiniteQueryObserver`; `CreateMutation` returns a `Mutation`; wires up `InvalidateKeys` subscriptions.
- `QueryCache` — a `ConcurrentDictionary<QueryKey, IQuery>` with timer-based eviction. Cache eviction uses `Observable.Timer` on the `IScheduler`; a pending eviction is cancelled if the same key is requested again before the timer fires. `GetOrCreate` is generic over `ICacheEntry`, so regular and infinite queries share one cache; a key already held by a different entry shape throws `InvalidOperationException`.
- `QueryObserver<TArgs,TData>` — the object returned to callers from `CreateQuery`. It is a key-switching proxy: each `SetArgs` call derives a new `QueryKey` via `KeyFactory`, calls `QueryCache.GetOrCreate`, and switches its `_activeQuery` subject to the matching `Query`. This is what enables deduplication — two observers with the same args share a single `Query` instance.
- `Query<TArgs,TData>` — the actual cached entry. Holds state as a `BehaviorSubject<QueryState<TData>>`. Uses `Observable.FromAsync(...).Switch()` so that a new invalidation cancels the previous in-flight fetch. When invalidated with no active subscribers, sets `_isStale = true`; the fetch is deferred until the first subscriber joins (stale-while-revalidate). `Cancel()` swaps in a fresh `CancellationTokenSource` so cancelling does not permanently poison the entry.
- `InfiniteQueryObserver<TArgs,TData,TPageParam>` / `InfiniteQuery<TArgs,TData,TPageParam>` — the paginated counterparts of `QueryObserver` / `Query`, following the same proxy-plus-cache-entry split and the same `Switch()`, stale-while-revalidate, and cancellation semantics. `InfiniteQuery` accumulates `_pages` / `_pageParams` and serves three commands (`RefetchAll`, `FetchNext`, `FetchPrevious`); `RefetchAll` re-fetches every currently loaded page under a single fetch span.
- `Mutation<TArgs,TData>` — same `Switch()` pattern for cancellation. Calls `OnMutate → Mutator → OnSuccess/OnFailure → OnSettled` in order.
- `QueryInstrumentation` / `QueryTelemetry` — OTel-compatible; uses only BCL `ActivitySource` and `Meter` APIs (no OpenTelemetry package required in the library itself).

**`IScheduler` injection** — all time-dependent code (eviction timers, refetch intervals, stale-time checks) uses an injected `IScheduler`. `QueryClientFactory.Create` defaults to `DefaultScheduler.Instance`. Tests inject `TestScheduler` from `Microsoft.Reactive.Testing` to control virtual time.

**`IRetryHandler`** — `DefaultRetryHandler` is a no-op pass-through (single attempt, no retry). Users supply their own implementation for actual retry logic.

**`IQueryClientInspector`** — internal interface implemented by `QueryClient` that exposes `IObservable<IReadOnlyList<IQueryInspector>> CacheEntries`. `IQueryInspector` extends `IQuery` and adds `MetricName`, `Status`, `CurrentData`, `LastUpdatedAt`, `ObserverCount`, and `StateChanged`. `QueryDevTools` casts `IQueryClient` to this interface at runtime.

**`ICacheEntry`** — internal interface extending `IQueryInspector` with the `Subscribed` / `Unsubscribed` signals `QueryCache` uses to drive eviction. Implemented only by the cached entries themselves (`Query`, `InfiniteQuery`) — the observers handed back to callers implement `IQueryInspector` but are not cache entries. Keep this split when adding a new query type: DevTools-facing inspection on `IQueryInspector`, cache lifecycle on `ICacheEntry`.

**Observability tagging** — metrics are tagged with a low-cardinality `MetricName` (the `Name` option, falling back to the first `QueryKey` part), never the full key, unless `QueryClientOptions.IncludeQueryKeyInMetrics` is set. Traces and logs always carry the full key; fetch spans additionally carry `query.name` for trace↔metric correlation. Fetch spans also carry a `FetchTrigger` (`manual` / `invalidate` / `interval` / `stale` / `prefetch`) and, for infinite queries, a `direction` (`refetch_all` / `next` / `previous`) plus a `query.pages` count. Duration histograms record seconds (OTel semantic conventions); log messages report milliseconds.

### Blazor layer

- `<Suspense>` — subscribes to `IQuery<TArgs,TData>.State` in `OnParametersSet`; renders `Content`, `Loading`, or `Failure` slots based on current state.
- `<Transition>` — similar to `<Suspense>` but keeps showing stale content during background re-fetches.
- `<InfiniteSuspense>` / `<InfiniteTransition>` — the `IInfiniteQuery` equivalents of the two components above. Unlike `<Suspense>` / `<Transition>`, their `Content` slot receives the whole `InfiniteQueryState<TData,TPageParam>` as context, so templates can render the accumulated `Pages` alongside the `IsFetchingNextPage` / `IsFetchingPreviousPage` flags.
- `<QueryRefreshMonitor>` — JS interop component; registers `visibilitychange` and `online` event listeners via `QueryRefreshMonitor.js` and calls `QueryClient.Invalidate(_ => true)` on focus/reconnect.
- `<QueryDevTools>` — live cache panel; subscribes to `IQueryClientInspector.CacheEntries`; uses `QueryDevTools.js` for drag-to-resize panel handles and theme persistence.

### DI registration

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.ExecutionMode = QueryExecutionMode.Ssr; // Scoped for SSR; Csr (default) = Singleton
    options.StaleTime = TimeSpan.FromMinutes(1);
});
```

### Versioning and packaging

Versions are derived by `MinVer` from git tags with prefix `v`. NuGet packages target `net10.0`. XML docs are generated for all non-test projects (`GenerateDocumentationFile`).

## Testing

Tests use [TUnit](https://tunit.dev/) (not xUnit/NUnit). Blazor component tests use [bUnit](https://bunit.dev/). Use `TUnit.Mocks` for mocking and `TUnit.Mocks.Logging` for logger mocks. `Microsoft.Reactive.Testing` provides `TestScheduler` for Rx time control.

To run the test suite the same way CI does (with coverage):
```bash
dotnet test --configuration Release --results-directory ./coverage -- --coverage --coverage-output-format cobertura
```

`./coverage` and `./TestResults` are both gitignored build output.

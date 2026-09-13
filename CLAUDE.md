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

## Build gates

Four independent gates guard the shipping surface, all wired up in `src/Directory.Build.props` (which
applies to `src/` only — tests and samples are untouched).

Two of them are switched per project rather than defaulted: `TrackPublicApi` and `IsAotCompatible` have no
default, and every `src/` project must set each explicitly in its own `.csproj`. The
`RequireExplicitProjectProperties` target fails the build if either is missing, so an opt-out is always a
visible decision in the project that makes it rather than a silent consequence of omission. When adding a
gate that works this way, extend that one target rather than adding a parallel one.

### Package validation (breaking changes)

`EnablePackageValidation` diffs each packed assembly against the last published release, set by
`PackageValidationBaselineVersion` (currently `2.0.0-beta.2`, uniform across all five packages). It runs on
`dotnet pack`, not `dotnet build`, and it compares the real assemblies against what consumers installed —
so it catches breaks the API text files cannot, including ones that only manifest on one target framework.

**Bump the baseline after every release.** For an intentional break, generate a suppression file rather than
weakening the gate:

```bash
dotnet pack -c Release -p:GenerateCompatibilitySuppressionFile=true
```

### Public API files (surface tracking)

Every `src/` project except `DotNetQuery.Blazor.DevTools` carries a `PublicAPI.Shipped.txt` /
`PublicAPI.Unshipped.txt` pair listing its entire public surface. Adding, removing, or changing a public
member fails the Release build (`RS0016` / `RS0017`) until the line is added to or removed from
`PublicAPI.Unshipped.txt`. DevTools sets `<TrackPublicApi>false</TrackPublicApi>` — it ships a drop-in
debugging component rather than an API consumers program against, so tracking it buys no compat guarantee
worth the Razor churn.

`PublicAPI.Shipped.txt` was seeded from the `v2.0.0-beta.2` surface, so a PR's diff to
`PublicAPI.Unshipped.txt` is exactly the API it adds. When cutting a release, move the accumulated
`Unshipped` lines into `Shipped` and leave `Unshipped` with just its `#nullable enable` header.

To regenerate entries after an API change, either apply the IDE code fix ("Add to public API") or run:

```bash
# repeat until the line count stops growing; the fixer applies one batch per pass
dotnet format analyzers DotNetQuery.slnx --diagnostics RS0016 --severity warn
```

`dotnet format` cannot fix `.razor` files, so `<Suspense>`-style component members must be added to
`src/DotNetQuery.Blazor/PublicAPI.Unshipped.txt` by hand — the `RS0016` message text is the exact line to paste.

Two rules are suppressed per-project, each with a comment in the `.csproj`: `RS0041` in `DotNetQuery.Blazor`
(the Razor-generated `BuildRenderTree` overrides use oblivious reference types) and `RS0026` in
`DotNetQuery.Mvvm` (`QueryViewModel` has two intentional constructor overloads with an optional dispatcher).

### Banned symbols (the `IScheduler` invariant)

`src/BannedSymbols.txt` — one shared list, pulled into every `src/` project as an `AdditionalFiles` entry via
`$(MSBuildThisFileDirectory)` — bans ambient time (`DateTime.Now` / `.UtcNow` / `.Today`,
`DateTimeOffset.Now` / `.UtcNow`) and blocking waits (`Thread.Sleep`, `Task.Delay`). All time must flow
through the injected `IScheduler` so tests can drive virtual time with `TestScheduler`; a stray
`DateTime.UtcNow` silently reintroduces wall-clock dependence that no test can control. Violations fail the
Release build as `RS0030`, quoting the per-symbol message from the file. Tests are unaffected — the gate is
scoped to `src/`, and test code is free to use real time.

To ban another API, add a line in documentation-comment ID form (`P:` property, `M:` method with the full
parameter list, `T:` type), followed by `;` and the message to show.

### Trim and AOT analyzers

`IsAotCompatible` turns on the trim, AOT, and single-file analyzers and stamps the assembly trimmable.
Blazor WASM trims by default on release publish and MAUI/UNO consumers publish AOT, so a trim-unsafe
construct here would otherwise surface as a runtime failure in a consumer's app rather than a build error in
ours.

`DotNetQuery.Blazor.DevTools` sets `<IsAotCompatible>false</IsAotCompatible>`. Its cache inspector
reflection-serializes arbitrary consumer data (`QueryDevTools.SerializeData` takes `object?`), which trips
`IL2026` / `IL3050` and which no source generation can make statically analyzable — the types belong to the
consumer. Under trimming `JsonSerializer` emits silently-empty JSON rather than throwing into the existing
`catch`, so claiming AOT compatibility there would be claiming something untrue. The other four projects are
genuinely trim- and AOT-clean; keep them that way rather than suppressing a new `IL` diagnostic.

### No NuGet lock files (deliberate)

This repo does **not** use `packages.lock.json`, and CI restores without `--locked-mode`. Central Package
Management already pins every direct dependency to an exact version in `Directory.Packages.props`, and NuGet
resolves transitives lowest-applicable, so the graph is deterministic without a lock file. Lock files are not
packed into the nupkg either — they would have protected only this repo's own build, not consumers.

What they cost was concrete: `IsAotCompatible=true` makes the SDK inject an implicit
`Microsoft.NET.ILLink.Tasks` `PackageReference` whose version is the SDK's *bundled runtime patch*. Nothing
here names that version, so a machine on a different SDK patch failed `--locked-mode` restore with `NU1004`
before a single line compiled — which in turn forced `global.json` to pin an exact SDK with
`rollForward: disable`, and left Dependabot to regenerate nine lock files per bump.

So if you are tempted to switch `RestorePackagesWithLockFile` back on, know that it also commits you to
pinning the SDK exactly and keeping that pin in step with `dotnet-sdk_10` in `flake.nix`.

## Architecture

The solution has five projects under `src/` and four test projects under `tests/`:

| Project | Purpose |
|---|---|
| `DotNetQuery.Core` | Core library: `IQueryClient`, `IQuery<TArgs,TData>`, `IInfiniteQuery<TArgs,TData,TPageParam>`, `IMutation<TArgs,TData>`, cache, observability |
| `DotNetQuery.Extensions.DependencyInjection` | `AddDotNetQuery()` extension; lifetime is Singleton (CSR) or Scoped (SSR) |
| `DotNetQuery.Blazor` | `<Suspense>`, `<Transition>`, `<InfiniteSuspense>`, `<InfiniteTransition>`, `<QueryRefreshMonitor>` Blazor components |
| `DotNetQuery.Blazor.DevTools` | `<QueryDevTools>` live cache inspector component |
| `DotNetQuery.Mvvm` | `QueryViewModel<TArgs,TData>` INPC wrapper with `IUiDispatcher` UI-thread marshaling, for MAUI/WPF/WinUI/UNO/Avalonia |

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

### Mvvm layer (`DotNetQuery.Mvvm`)

Three view models — `QueryViewModel<TArgs,TData>`, `InfiniteQueryViewModel<TArgs,TData,TPageParam>`,
`MutationViewModel<TArgs,TData>` — wrap the correspondingly-named Core type and expose its state as
bindable `INotifyPropertyChanged` properties. All three share the same mechanics, factored into one
internal helper rather than copied three times:

- `UiStateBinding<TState>` (`internal sealed`) — owns the state subscription, the
  latest-wins coalescing (a burst of emissions collapses into one dispatcher `Post`), and the
  atomic-swap-before-diffed-raise sequencing (`Current` is updated *before* `apply(previous, next)`
  runs, so a handler reading a sibling property never sees a torn state). Each view model holds one
  and forwards `ApplyState` into it; `Current` backs every bindable property.
- Ownership follows the constructor used: wrapping an existing query/mutation (or `ToViewModel()`,
  which is sugar for that constructor) leaves ownership with the caller — `Dispose()` releases only
  the subscription. The `IQueryClient` + options constructor makes the view model own what it
  creates — `Dispose()` disposes it too. Holding the live subscription is what retains a query's
  cache entry; a forgotten dispose pins it indefinitely.
- `StateChanged` (`event EventHandler?`) — raised on the UI thread after every `PropertyChanged` for
  an applied state, on all three view models. The hook for page-specific work that needs to run once
  per state change without a second raw subscription — most commonly syncing an
  `ObservableCollection` via `SyncFrom` (see below).
- `MutationViewModel.ExecuteCommand` / `ToCommand(...)` guard against double-submit by reading the
  wrapped `IMutation.CurrentState` directly (not the dispatcher-marshaled `CurrentState` the view
  model itself exposes) — this closes the gap between a click starting a run and that run's state
  reaching the view model one dispatcher hop later, and also disables the command when a run starts
  directly on a shared `IMutation`. `ToCommand<TParam>` (parameter maps to `TArgs`) and
  `ToCommand(Func<TArgs>, ...)` (args come from the page VM) both return `IMutationCommand`
  (`ICommand` + public `RaiseCanExecuteChanged()`) — call `ToCommand` once per command, typically in
  a constructor; each call permanently appends to an internal refresh list with no removal path, so
  calling it per-row in a list leaks. `ExecuteCommand.Execute(object?)` casts strictly to `TArgs` —
  throws `ArgumentException` naming both types on a mismatch, never coerces.
- `IUiDispatcher.Post` is what all of the above marshal through (default: `SynchronizationContextUiDispatcher`
  capturing `SynchronizationContext.Current` at construction; null context invokes inline for
  tests/console). **Event streams (`Success`/`Failure`/`Settled` on either a query or a mutation)
  must *not* use this coalescing path** — `ObserveOnUi()` (`QueryViewModelExtensions`) exists
  specifically for them: it posts one callback per emission, nothing dropped or collapsed, with the
  dispatcher captured when `ObserveOnUi` is *called* (not when the returned observable is
  subscribed). Named `ObserveOnUi`, not `SubscribeOnUi`, because Rx's `SubscribeOn` means something
  unrelated.
- `QueryViewModelExtensions.ToViewModel()` — three overloads, one per query/mutation interface;
  always wraps (never takes ownership), despite the name not saying so on its own.
- `ObservableCollectionExtensions.SyncFrom` — reconciles an `ObservableCollection<T>` in place
  (`Move`/`Insert`/`Remove`, and same-type-overload-only `Replace`) against a new source sequence,
  rather than `Clear()` + re-add, which drops whatever a bound control tracks by position (e.g. a
  list box's selection). Two overloads: same-type (`itemComparer` decides whether a matched item
  needs `Replace`ing — there's no in-place update for a plain `T`) and projection
  (`TSource`→`T`, e.g. building row view models from models; `update` refreshes a matched row in
  place instead of ever replacing it, which is what lets a bound selection survive a refetch — see
  `TodoDetailsViewModel`/`TodoItemViewModel` in the Avalonia sample). Worst case O(n²) moves,
  documented as acceptable only for UI-sized lists. Duplicate keys in `source` throw
  `ArgumentException`.
- `BindableBase` — public minimal INPC base (deliberately not named `ObservableObject` to avoid
  CommunityToolkit clashes). `RelayCommand` is `internal` for the same reason;
  `IMutationCommand.RaiseCanExecuteChanged()` is deliberately *not* named
  `NotifyCanExecuteChanged()` either, so CommunityToolkit's `[NotifyCanExecuteChangedFor]` codegen
  doesn't silently target it. Zero dependencies beyond `DotNetQuery.Core`.

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

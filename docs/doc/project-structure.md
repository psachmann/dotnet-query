# Project Structure

Here is a map of the repository to help you find your way around.

```
dotnet-query/
├── src/
│   ├── DotNetQuery.Core/                          Core library
│   ├── DotNetQuery.Blazor/                        Blazor components
│   └── DotNetQuery.Extensions.DependencyInjection/ DI integration
├── tests/
│   ├── DotNetQuery.Core.Tests/
│   ├── DotNetQuery.Blazor.Tests/
│   └── DotNetQuery.Extensions.DependencyInjection.Tests/
├── docs/                                           DocFX documentation
├── Directory.Build.props                           Shared MSBuild properties
├── Directory.Packages.props                        Centralized NuGet versions
├── global.json                                     .NET SDK version pin
└── flake.nix                                       Nix dev environment
```

## src/DotNetQuery.Core

The core library. No dependencies outside of System.Reactive.

```
DotNetQuery.Core/
├── IQuery.cs               IQuery (base) and IQuery<TArgs, TData> interfaces
├── IMutation.cs            IMutation<TArgs, TData> interface
├── IQueryClient.cs         IQueryClient interface
├── IRetryHandler.cs        IRetryHandler interface
├── QueryKey.cs             Immutable cache key
├── QueryStatus.cs          QueryStatus enum
├── QueryState.cs           QueryState<TData> record with factory methods
├── MutationStatus.cs       MutationStatus enum
├── MutationState.cs        MutationState<TData> record with factory methods
├── QueryOptions.cs         Per-query configuration record
├── MutationOptions.cs      Per-mutation configuration record
├── QueryClientOptions.cs   Global client configuration record
├── QueryExecutionMode.cs   CSR / SSR mode enum
├── QueryClientFactory.cs   Static factory for IQueryClient
└── Internals/
    ├── QueryClient.cs       IQueryClient implementation; owns the cache
    ├── QueryCache.cs        Concurrent cache with eviction logic
    ├── Query.cs             Single cache entry; manages fetch lifecycle
    ├── QueryObserver.cs     Wraps Query; manages args and enabled state
    ├── Mutation.cs          IMutation implementation
    ├── DefaultRetryHandler.cs  Exponential backoff (1s, 2s, 4s)
    └── EffectiveQueryOptions.cs Merges global + per-query options
```

### Public API vs Internals

Everything in `Internals/` is `internal sealed` — it is an implementation detail and not part of the public API. You interact with the library entirely through the interfaces and records in the root of `DotNetQuery.Core`.

## src/DotNetQuery.Blazor

Razor components for Blazor apps. Depends on `DotNetQuery.Core` and `Microsoft.AspNetCore.Components.Web`.

```
DotNetQuery.Blazor/
├── Suspense.razor      Shows loading/content/failure; hides old data while refetching
├── Transition.razor    Stale-while-revalidate; shows old data during background refetch
└── _Imports.razor      Shared using directives for the Blazor package
```

## src/DotNetQuery.Extensions.DependencyInjection

DI integration. Depends on `DotNetQuery.Core` and `Microsoft.Extensions.DependencyInjection`.

```
DotNetQuery.Extensions.DependencyInjection/
└── ServiceCollectionExtensions.cs   AddDotNetQuery() extension method
```

## tests/

Each test project mirrors its production counterpart and uses [TUnit](https://tunit.dev/) as the test runner, with [bunit](https://bunit.dev/) for Blazor component tests.

```
tests/
├── DotNetQuery.Core.Tests/
│   ├── QueryTests.cs          Query lifecycle, state transitions, caching
│   ├── MutationTests.cs       Mutation execution, callbacks, cancellation
│   ├── QueryClientTests.cs    Client-level invalidation, deduplication
│   ├── QueryCacheTests.cs     Cache eviction and timing
│   ├── QueryKeyTests.cs       Equality, hashing, From() validation
│   └── RetryHandlerTests.cs   Exponential backoff, cancellation
├── DotNetQuery.Blazor.Tests/
│   ├── SuspenseTests.cs       Suspense component rendering states
│   └── TransitionTests.cs     Transition stale-while-revalidate behavior
└── DotNetQuery.Extensions.DependencyInjection.Tests/
    └── ServiceCollectionExtensionsTests.cs  DI registration and lifetime
```

## docs/

Documentation source for the DocFX site.

```
docs/
├── docfx.json          DocFX build configuration
├── index.md            Landing page
├── toc.yml             Top-level navigation (Docs | API)
├── doc/
│   ├── toc.yml         Docs section navigation
│   ├── introduction.md
│   ├── getting-started.md
│   ├── guides/
│   │   ├── queries.md
│   │   ├── mutations.md
│   │   ├── caching.md
│   │   ├── blazor.md
│   │   ├── retries.md
│   │   └── ssr.md
│   ├── examples/
│   │   ├── queries.md
│   │   ├── mutations.md
│   │   └── blazor.md
│   ├── project-structure.md
│   └── contributing.md
└── api/                Auto-generated API reference (do not edit manually)
```

## Key Configuration Files

| File | Purpose |
|------|---------|
| `Directory.Build.props` | Shared MSBuild properties: XML doc generation, versioning (MinVer), SourceLink, symbol packages, release-build warnings-as-errors. |
| `Directory.Packages.props` | Centralized NuGet package versions — all `PackageReference` entries in `.csproj` files omit the version and inherit from here. |
| `global.json` | Pins the .NET SDK version to ensure reproducible builds across machines and CI. |
| `flake.nix` | Nix flake for a fully reproducible dev environment including the SDK, CSharpier, and DocFX. Activate with `direnv allow`. |
| `.github/workflows/build.yaml` | CI pipeline: format check → Release build → test with coverage → Codecov upload. |
| `.github/dependabot.yml` | Weekly automated dependency updates for NuGet and GitHub Actions. |

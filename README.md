# DotNet Query

[![Build](https://github.com/psachmann/dotnet-query/actions/workflows/build.yaml/badge.svg)](https://github.com/psachmann/dotnet-query/actions/workflows/build.yaml)
[![codecov](https://codecov.io/gh/psachmann/dotnet-query/graph/badge.svg)](https://codecov.io/gh/psachmann/dotnet-query)

> A [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor.

DotNet Query brings the proven data-fetching patterns of TanStack Query to the .NET ecosystem. It provides a structured, reactive approach to managing asynchronous data, caching, and mutations — removing boilerplate and giving you predictable loading, error, and success states out of the box.

## Features

- **Queries** — fetch async data with automatic caching, background refetching, and stale-while-revalidate semantics
- **Mutations** — execute data-modifying operations with lifecycle callbacks and automatic cache invalidation
- **Reactive state** — built on [Rx.NET](https://github.com/dotnet/reactive), every query and mutation exposes `IObservable` streams for composable async pipelines
- **Retry logic** — configurable retry strategies with exponential backoff out of the box
- **Query deduplication** — identical keys share a single cached query instance
- **CSR / SSR modes** — `QueryExecutionMode` controls singleton (WebAssembly) vs scoped (Server-Side Rendering) lifetime
- **DI integration** — first-class support for `Microsoft.Extensions.DependencyInjection`
- **Blazor components** — `<Suspense>` and `<Transition>` components for declarative query rendering in Blazor

## Documentation

Full API reference and usage guides are published via [docfx](https://dotnet.github.io/docfx/) and available at *(link TBD)*.

## Developer Documentation

### Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0 (see [global.json](global.json)) |
| CSharpier | 1.2.6 (installed as local tool) |

A [Nix flake](flake.nix) is provided for reproducible dev environments. With [direnv](https://direnv.net/) installed, run `direnv allow` in the repo root to activate it automatically.

### Getting Started

```bash
# Restore tools and packages
dotnet tool restore
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Check formatting
dotnet csharpier check .
```

### Project Structure

```
src/
  DotNetQuery.Core/                          Core interfaces and implementations
  DotNetQuery.Blazor/                        Blazor components (Suspense, Transition)
  DotNetQuery.Extensions.DependencyInjection/ DI extension methods
tests/
  DotNetQuery.Core.Tests/
  DotNetQuery.Extensions.DependencyInjection.Tests/
```

### CI / CD

The [build pipeline](.github/workflows/build.yaml) runs on every push and pull request to `main`:

1. Restore tools and packages
2. CSharpier format check
3. Release build
4. Tests with code coverage (Cobertura)
5. Coverage upload to Codecov

Dependency updates are managed automatically via [Dependabot](.github/dependabot.yml) (weekly, for NuGet and GitHub Actions).

## License

[MIT](LICENSE) — Copyright (c) 2026 Patrick Sachmann

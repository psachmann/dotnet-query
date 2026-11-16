# Fetchling

[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/psachmann/fetchling/build.yaml)
](https://github.com/psachmann/fetchling/actions/workflows/build.yaml)
[![Codecov (with branch)](https://img.shields.io/codecov/c/github/psachmann/fetchling/main)](https://codecov.io/gh/psachmann/fetchling)
[![NuGet Version](https://img.shields.io/nuget/v/fetchling.core)
](https://www.nuget.org/packages?q=fetchling+psachmann)
[![GitHub License](https://img.shields.io/github/license/psachmann/fetchling)](https://github.com/psachmann/fetchling/blob/main/LICENSE.md)

> A [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor.

> [!NOTE]
> **Formerly DotNetQuery.** Fetchling is the same library, renamed for 3.0. The `DotNetQuery.*` packages stay on 2.x and get no further releases. To upgrade, switch each `DotNetQuery.*` package to its `Fetchling.*` counterpart and replace `DotNetQuery` with `Fetchling` in namespaces and in `AddDotNetQuery()`.

Fetchling brings the battle-tested data-fetching patterns of TanStack Query to the .NET ecosystem. It gives you predictable loading, error, and success states out of the box — powered by [Rx.NET](https://github.com/dotnet/reactive) observables, so everything is composable, lazy, and reactive by default.

## Table of Contents

- [Fetchling](#fetchling)
  - [Table of Contents](#table-of-contents)
  - [Features](#features)
  - [Installation](#installation)
  - [Documentation](#documentation)
  - [Quick Start](#quick-start)
  - [Contributing](#contributing)
  - [License](#license)

## Features

- **Queries** — fetch async data with automatic caching, background refetching, and stale-while-revalidate semantics
- **Infinite queries** — paginated and "load more" data fetching with `<InfiniteSuspense>` / `<InfiniteTransition>`
- **Streamed queries** — fold an `IAsyncEnumerable` (gRPC streams, LLM tokens, SignalR or server-sent events) into data that renders as it grows
- **Mutations** — execute data-modifying operations with lifecycle callbacks, progress reporting, and automatic cache invalidation on success
- **Live data** — keep the cache current from server pushes: invalidate on an event, or write the pushed data straight in with `IQueryClient.SetData`
- **Reactive state** — built on [Rx.NET](https://github.com/dotnet/reactive), every query and mutation exposes `IObservable` streams for composable async pipelines
- **Smart caching** — configurable stale time and cache time control when data is re-fetched and when it is evicted
- **Query deduplication** — identical keys share a single cached query instance; no redundant requests
- **Retry logic** — one attempt by default, fail-fast; plug in exponential backoff or any other policy via `IRetryHandler`
- **Observability** — OTel-compatible distributed tracing (`ActivitySource`), metrics (`Meter`), and structured logging (`ILogger`) using only BCL APIs; no OpenTelemetry package required in the library
- **Blazor components** — `<Suspense>` and `<Transition>` components for declarative query rendering
- **DevTools** — `<QueryDevTools>` live cache inspector component for development, and a public inspection API for building your own
- **MVVM view models** — `QueryViewModel<TArgs, TData>` with bindable `INotifyPropertyChanged` properties and UI-thread marshaling for MAUI, WPF, WinUI, UNO Platform, and Avalonia
- **CSR / SSR support** — `QueryExecutionMode` controls Singleton (WebAssembly) vs Scoped (Server-Side Rendering) DI lifetime
- **DI integration** — first-class support for `Microsoft.Extensions.DependencyInjection`

## Installation

```bash
# Core + DI integration in one package — the usual starting point
dotnet add package Fetchling

# Core library — always required
dotnet add package Fetchling.Core

# DI integration
dotnet add package Fetchling.Extensions.DependencyInjection

# Blazor components
dotnet add package Fetchling.Blazor

# Blazor DevTools — live cache inspector for development
dotnet add package Fetchling.Blazor.DevTools

# MVVM view models (MAUI, WPF, WinUI, UNO, Avalonia)
dotnet add package Fetchling.Mvvm
```

## Documentation

Full guides, examples, and API reference are available **[here](https://psachmann.github.io/fetchling/)**.

- [Introduction](https://psachmann.github.io/fetchling/doc/introduction.html) — core concepts and motivation
- [Getting Started](https://psachmann.github.io/fetchling/doc/getting-started.html) — install and write your first query
- [Guides](https://psachmann.github.io/fetchling/doc/guides/queries.html) — queries, mutations, caching, Blazor, retries, and SSR
- [Migrating to v3](https://psachmann.github.io/fetchling/doc/migrating-to-v3.html) — breaking changes and upgrade steps for 3.0
- [Migrating to v2](https://psachmann.github.io/fetchling/doc/migrating-to-v2.html) — breaking changes and upgrade steps for 2.0
- [API Reference](https://psachmann.github.io/fetchling/api/Fetchling.Core.html) — full generated API docs
- [Blazor sample](https://github.com/psachmann/fetchling/tree/main/samples/Fetchling.Samples.Blazor) and [Avalonia sample](https://github.com/psachmann/fetchling/tree/main/samples/Fetchling.Samples.Avalonia) — complete, runnable apps built on the library

> **AI agents / LLMs:** a single-file reference covering the full public API, all patterns, and architecture notes is available at [`/llms.txt`](https://psachmann.github.io/fetchling/llms.txt). The docs are also indexed on [Context7](https://context7.com/psachmann/fetchling) under the library ID `/psachmann/fetchling`.

## Quick Start

```csharp
// Program.cs
builder.Services.AddFetchling(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(1);
});
```

```csharp
// Create a query
var query = queryClient.CreateQuery(new QueryOptions<int, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => userService.GetByIdAsync(id, ct),
});

query.SetArgs(42);

query.Success.Subscribe(user => Console.WriteLine($"Hello, {user.Name}!"));
query.Failure.Subscribe(error => Console.WriteLine($"Oops: {error.Message}"));
```

```csharp
// Create a mutation
var mutation = queryClient.CreateMutation(new MutationOptions<CreateUserRequest, UserDto>
{
    Mutator        = (req, ct) => userService.CreateAsync(req, ct),
    InvalidateKeys = [QueryKey.From("users")],
    OnSuccess      = (_, user) => Console.WriteLine($"Created {user.Name}"),
});

mutation.Execute(new CreateUserRequest { Name = "Alice" });
```

```html
<!-- Blazor -->
<Suspense Query="query">
    <Content Context="user"><p>Hello, @user.Name!</p></Content>
    <Loading><p>Loading...</p></Loading>
    <Failure Context="error"><p>Error: @error.Message</p></Failure>
</Suspense>
```

## Contributing

Contributions are welcome! Please see the [Contributing guide](https://psachmann.github.io/fetchling/doc/contributing.html) for setup instructions, coding conventions, and the PR workflow. For bugs and feature requests, [open an issue](https://github.com/psachmann/fetchling/issues).

## License

[MIT](https://github.com/psachmann/fetchling/blob/main/LICENSE.md) — Copyright (c) 2026 Patrick Sachmann

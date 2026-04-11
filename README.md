# DotNet Query

[![Build](https://github.com/psachmann/dotnet-query/actions/workflows/build.yaml/badge.svg)](https://github.com/psachmann/dotnet-query/actions/workflows/build.yaml)
[![codecov](https://codecov.io/gh/psachmann/dotnet-query/graph/badge.svg)](https://codecov.io/gh/psachmann/dotnet-query)

> A [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor.

DotNet Query brings the battle-tested data-fetching patterns of TanStack Query to the .NET ecosystem. It gives you predictable loading, error, and success states out of the box — powered by [Rx.NET](https://github.com/dotnet/reactive) observables, so everything is composable, lazy, and reactive by default.

## Features

- **Queries** — fetch async data with automatic caching, background refetching, and stale-while-revalidate semantics
- **Mutations** — execute data-modifying operations with lifecycle callbacks and automatic cache invalidation on success
- **Reactive state** — built on [Rx.NET](https://github.com/dotnet/reactive), every query and mutation exposes `IObservable` streams for composable async pipelines
- **Smart caching** — configurable stale time and cache time control when data is re-fetched and when it is evicted
- **Query deduplication** — identical keys share a single cached query instance; no redundant requests
- **Retry logic** — exponential backoff out of the box; plug in your own strategy via `IRetryHandler`
- **Blazor components** — `<Suspense>` and `<Transition>` components for declarative query rendering
- **CSR / SSR support** — `QueryExecutionMode` controls Singleton (WebAssembly) vs Scoped (Server-Side Rendering) DI lifetime
- **DI integration** — first-class support for `Microsoft.Extensions.DependencyInjection`

## Installation

```bash
# Core library — always required
dotnet add package DotNetQuery.Core

# DI integration
dotnet add package DotNetQuery.Extensions.DependencyInjection

# Blazor components
dotnet add package DotNetQuery.Blazor
```

## Documentation

Full guides, examples, and API reference are available **[here](https://psachmann.github.io/dotnet-query/)**.

- [Introduction](https://psachmann.github.io/dotnet-query/doc/introduction.html) — core concepts and motivation
- [Getting Started](https://psachmann.github.io/dotnet-query/doc/getting-started.html) — install and write your first query
- [Guides](https://psachmann.github.io/dotnet-query/doc/guides/queries.html) — queries, mutations, caching, Blazor, retries, and SSR
- [API Reference](https://psachmann.github.io/dotnet-query/api/DotNetQuery.Core.html) — full generated API docs

## Quick Start

```csharp
// Program.cs
builder.Services.AddDotNetQuery(options =>
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

```razor
<!-- Blazor -->
<Suspense Query="_userQuery">
    <Content Context="user"><p>Hello, @user.Name!</p></Content>
    <Loading><p>Loading...</p></Loading>
    <Failure Context="error"><p>Error: @error.Message</p></Failure>
</Suspense>
```

## Contributing

Contributions are welcome! Please see the [Contributing guide](https://psachmann.github.io/dotnet-query/doc/contributing.html) for setup instructions, coding conventions, and the PR workflow. For bugs and feature requests, [open an issue](https://github.com/psachmann/dotnet-query/issues).

## License

[MIT](LICENSE) — Copyright (c) 2026 Patrick Sachmann

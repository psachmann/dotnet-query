---
_layout: landing
---

# DotNet Query

> A [TanStack Query](https://tanstack.com/query)-inspired async data fetching and state management library for .NET and Blazor.

DotNet Query brings the battle-tested data-fetching patterns of TanStack Query to the .NET ecosystem. It gives you predictable loading, error, and success states out of the box — powered by [Rx.NET](https://github.com/dotnet/reactive) observables, so everything is composable, lazy, and reactive by default.

If you have ever found yourself writing the same `isLoading / hasError / data` boilerplate over and over, or wrestling with cache invalidation after a form submission, DotNet Query is here to help.

---

## Features

- **Queries** — fetch async data with automatic caching, background refetching, and stale-while-revalidate semantics.
- **Mutations** — execute data-modifying operations with lifecycle callbacks and automatic cache invalidation on success.
- **Reactive state** — every query and mutation exposes `IObservable` streams for composable async pipelines.
- **Smart caching** — configurable stale time and cache time control when data is refetched and when it is evicted.
- **Query deduplication** — identical keys share a single cached query instance; no redundant requests.
- **Retry logic** — exponential backoff out of the box; plug in your own strategy via `IRetryHandler`.
- **Blazor components** — `<Suspense>` and `<Transition>` components for declarative query rendering.
- **CSR / SSR support** — `QueryExecutionMode` controls Singleton (WebAssembly) vs Scoped (Server-Side Rendering) DI lifetime.
- **DI integration** — first-class support for `Microsoft.Extensions.DependencyInjection`.

---

## Quick Example

```csharp
// Register in Program.cs
builder.Services.AddDotNetQuery(options =>
{
    options.StaleTime = TimeSpan.FromMinutes(1);
});

// In your service or component
var query = queryClient.CreateQuery(new QueryOptions<int, UserDto>
{
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher    = (id, ct) => userService.GetByIdAsync(id, ct),
});

query.Args.OnNext(42);

query.Success.Subscribe(user => Console.WriteLine($"Hello, {user.Name}!"));
query.Failure.Subscribe(error => Console.WriteLine($"Oops: {error.Message}"));
```

---

## Get Started

- [Introduction](doc/introduction.md) — understand the core concepts and motivation.
- [Getting Started](doc/getting-started.md) — install and write your first query in minutes.
- [Guides](doc/guides/queries.md) — deep dives into queries, mutations, caching, Blazor, and more.
- [API Reference](api/DotNetQuery.Core.yml) — full generated API documentation.

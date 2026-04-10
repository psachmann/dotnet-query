# Server-Side Rendering

DotNet Query supports both Client-Side Rendering (CSR/WebAssembly) and Server-Side Rendering (SSR/Blazor Server) through `QueryExecutionMode`. The mode controls the DI lifetime of `IQueryClient`.

## Why Lifetime Matters

In a CSR (WebAssembly) app, there is a single user per process, so a **Singleton** `IQueryClient` works perfectly — everyone shares the same cache.

In a SSR app (Blazor Server or static SSR with interactivity), the server handles multiple users concurrently. A Singleton cache would mix data between users, which is both a bug and a security issue. Each user needs their own isolated `IQueryClient`, which maps naturally to a **Scoped** lifetime (one per HTTP request / SignalR circuit).

## Configuring the Execution Mode

### CSR (WebAssembly) — Singleton

This is the default. You do not need to set anything explicitly:

```csharp
builder.Services.AddDotNetQuery(); // Singleton by default
```

Or explicitly:

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.ExecutionMode = QueryExecutionMode.Csr;
});
```

### SSR (Blazor Server / Scoped) — Scoped

```csharp
builder.Services.AddDotNetQuery(options =>
{
    options.ExecutionMode = QueryExecutionMode.Ssr;
});
```

With `QueryExecutionMode.Ssr`, `IQueryClient` is registered with **Scoped** lifetime. In Blazor Server, a scope is created per SignalR circuit, so each user gets their own isolated client and cache.

## Blazor Auto Render Mode

If your app uses Blazor's "Auto" render mode (starts with SSR, switches to WebAssembly), be aware that:

- During the SSR prerender phase, the Scoped `IQueryClient` is used.
- After the WebAssembly upgrade, the Singleton client (from the client-side DI container) is used.

Both containers need `AddDotNetQuery` registered with their respective modes:

```csharp
// Server project (Program.cs)
builder.Services.AddDotNetQuery(options =>
{
    options.ExecutionMode = QueryExecutionMode.Ssr;
});

// Client project (Program.cs)
builder.Services.AddDotNetQuery(options =>
{
    options.ExecutionMode = QueryExecutionMode.Csr;
});
```

## Using a Custom Scheduler in Tests

The optional `IScheduler` parameter accepted by `QueryClientFactory.Create` is primarily useful in tests. By injecting a `TestScheduler` (from `Microsoft.Reactive.Testing`), you can control time and verify stale-time and cache-time behavior without actually waiting:

```csharp
var scheduler = new TestScheduler();
var client    = QueryClientFactory.Create(new QueryClientOptions(), scheduler);

// Advance time in tests
scheduler.AdvanceBy(TimeSpan.FromMinutes(5).Ticks);
```

In production the scheduler defaults to `DefaultScheduler.Instance`, which uses real wall-clock time. You do not need to register a scheduler in DI for normal use.

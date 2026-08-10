# Observability

DotNet Query emits distributed traces, metrics, and structured log messages out of the box. It uses only BCL APIs — `System.Diagnostics.ActivitySource`, `System.Diagnostics.Metrics.Meter`, and `Microsoft.Extensions.Logging.ILogger` — so no OpenTelemetry package is required in the library itself. Consumers wire up collection on their side and the standard hooks are picked up automatically.

## How It Works

All telemetry flows through a single public entry point:

```csharp
// DotNetQuery.Core.Observability
public static class QueryTelemetry
{
    public const string SourceName = "DotNetQuery";
    public static readonly ActivitySource ActivitySource = new(SourceName);
    public static readonly Meter Meter = new(SourceName);
}
```

`QueryTelemetry.SourceName` (`"DotNetQuery"`) is the name you use when subscribing to traces or metrics in your app.

## Enabling Logging

Pass a logger when creating the client. With DI the `ILoggerFactory` is resolved automatically:

```csharp
// DI (recommended) — no extra configuration needed
builder.Services.AddDotNetQuery();
```

Without DI, pass a logger to the factory:

```csharp
ILoggerFactory loggerFactory = LoggerFactory.Create(b => b.AddConsole());
ILogger logger = loggerFactory.CreateLogger(QueryTelemetry.SourceName);

IQueryClient client = QueryClientFactory.Create(new QueryClientOptions(), logger: logger);
```

## Enabling OpenTelemetry

Add the OpenTelemetry packages to your **app** project (not to the library):

```bash
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Console  # or any other exporter
```

Then subscribe to the `"DotNetQuery"` source in `Program.cs`:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(QueryTelemetry.SourceName)
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddMeter(QueryTelemetry.SourceName)
        .AddConsoleExporter());
```

Any OpenTelemetry-compatible exporter works — Jaeger, Zipkin, OTLP, Prometheus, Azure Monitor, etc.

## Naming Queries and Mutations

Set `Name` on `QueryOptions<TArgs, TData>` or `MutationOptions<TArgs, TData>` to control the low-cardinality
identifier used to tag metrics:

```csharp
var options = new QueryOptions<int, User>
{
    Name = "users", // shows up as the query.name tag on metrics
    KeyFactory = id => QueryKey.From("users", id),
    Fetcher = (id, ct) => userApi.GetAsync(id, ct),
};
```

When `Name` is not set, DotNet Query falls back to the first part of the derived `QueryKey`
(e.g. `"users"` for a key built as `QueryKey.From("users", id)`). Mutations without a `Name`
fall back to `typeof(TArgs).Name`.

## Traces

DotNet Query creates one activity span per operation:

| Span name | When |
|---|---|
| `query.fetch` | Every time a query fetches data |
| `mutation.execute` | Every time a mutation runs |

### Query fetch span

The `query.fetch` span carries the full identity of the fetch — traces are not subject to the
cardinality limits that apply to metrics, so the complete `QueryKey` is always included:

| Tag | Value |
|---|---|
| `query.key` | The string representation of the `QueryKey` (e.g. `users:42`) |
| `query.name` | The low-cardinality metric name — the same value metrics are tagged with, for trace↔metric correlation |
| `trigger` | What caused the fetch: `manual`, `invalidate`, `interval`, `stale`, or `prefetch` |
| `direction` | Infinite queries only: `refetch_all`, `next`, or `previous` |
| `query.pages` | Infinite queries only: number of pages fetched (`1` for `next`/`previous`, page count for `refetch_all`) |
| `attempts` | Number of attempts made by the configured `IRetryHandler` (`1` when no retry occurred) |
| `otel.status_code` | `Ok` on success, `Error` on failure or cancellation |
| `error.type` | Exception type name (only on failure) |

A `retry` activity event is added for each attempt beyond the first.

Infinite queries emit the same `query.fetch` span as regular queries. A `refetch_all` re-fetches every
currently loaded page under a single span, so its `attempts` counts retries across all of those page
fetches — it is `1` when every page succeeded first try.

### Mutation execute span

| Tag | Value |
|---|---|
| `mutation.name` | The mutation's `Name`, or `typeof(TArgs).Name` when unset |
| `attempts` | Number of attempts made by the configured `IRetryHandler` |
| `otel.status_code` | `Ok` on success, `Error` on failure or cancellation |
| `error.type` | Exception type name (only on failure) |

## Metrics

All metrics use the `"DotNetQuery"` meter name. Attach a tag filter in your metrics pipeline if needed.

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `dotnetquery.query.duration` | Histogram | s | Duration of each query fetch operation |
| `dotnetquery.query.active` | UpDownCounter | — | Number of query fetch operations currently in flight |
| `dotnetquery.query.retries` | Counter | — | Retry attempts made by query fetches beyond the first |
| `dotnetquery.cache.hits` | Counter | — | Cache lookups that found an existing entry |
| `dotnetquery.cache.misses` | Counter | — | Cache lookups that created a new entry |
| `dotnetquery.cache.entries` | UpDownCounter | — | Entries currently held in the query cache |
| `dotnetquery.cache.evictions` | Counter | — | Entries automatically evicted after `CacheTime` elapsed |
| `dotnetquery.mutation.duration` | Histogram | s | Duration of each mutation operation |

Duration histograms record **seconds**, following current OTel semantic conventions for
`*.duration` instruments. Log messages report milliseconds for readability.

`dotnetquery.cache.entries` is also decremented when a client (and its cache) is disposed, so
scoped SSR clients do not leak their live-entry count into the process-wide gauge.
| `dotnetquery.mutation.retries` | Counter | — | Retry attempts made by mutations beyond the first |

### Tags on metrics

| Metric | Tags |
|---|---|
| `dotnetquery.query.duration` | `query.name`, `status` (`success` / `failure` / `cancelled`), `error.type` (on failure), `trigger` |
| `dotnetquery.query.active` | `query.name` |
| `dotnetquery.query.retries` | `query.name` |
| `dotnetquery.cache.hits` | `query.name` |
| `dotnetquery.cache.misses` | `query.name` |
| `dotnetquery.cache.entries` | `query.name` |
| `dotnetquery.cache.evictions` | `query.name` |
| `dotnetquery.mutation.duration` | `mutation.name`, `status` (`success` / `failure` / `cancelled`), `error.type` (on failure) |
| `dotnetquery.mutation.retries` | `mutation.name` |

### Cardinality

Metrics are tagged with `query.name` (or `mutation.name`), never with the full `QueryKey`. A `QueryKey`
typically embeds per-entity arguments — `QueryKey.From("users", id)` — and tagging metrics with it
would create one time series per distinct `id`. Most metrics backends enforce a cardinality limit per
stream (OpenTelemetry defaults to 2000); exceeding it silently collapses new series into an overflow
bucket. Traces and log messages are not affected — they always carry the full key.

If your keys are drawn from a bounded, known-small set and you want the full key on metrics anyway,
set `QueryClientOptions.IncludeQueryKeyInMetrics = true`. This adds a `query.key` tag alongside
`query.name` on every metric. Leave it `false` (the default) for any key space that includes
per-entity identifiers.

## Log Messages

All log messages use the category `"DotNetQuery"` (the same string as `QueryTelemetry.SourceName`).

| Level | Message |
|---|---|
| Debug | `Fetch started for key '{QueryKey}'` |
| Debug | `Fetch succeeded for key '{QueryKey}' in {Duration}ms` |
| Warning | `Fetch failed for key '{QueryKey}' after {Duration}ms` (+ exception) |
| Debug | `Fetch cancelled for key '{QueryKey}'` |
| Debug | `Cache hit for key '{QueryKey}'` |
| Debug | `Cache miss for key '{QueryKey}'` |
| Debug | `Cache entry for key '{QueryKey}' evicted after CacheTime elapsed` |
| Debug | `Cache entry for key '{QueryKey}' released on cache dispose` |
| Debug | `Mutation '{MutationName}' started` |
| Debug | `Mutation '{MutationName}' succeeded in {Duration}ms` |
| Warning | `Mutation '{MutationName}' failed after {Duration}ms` (+ exception) |
| Debug | `Mutation '{MutationName}' cancelled` |

Log messages are source-generated via `[LoggerMessage]`, so Debug-level calls cost nothing beyond an
`IsEnabled` check when the category is filtered out.

## Filtering Log Output

Because all messages share the `"DotNetQuery"` category, you can control verbosity with a single filter:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "DotNetQuery": "Warning"
    }
  }
}
```

This suppresses the Debug-level fetch/cache messages and keeps only warnings (failures and retries).

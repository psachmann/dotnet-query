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

## Traces

DotNet Query creates one activity span per operation:

| Span name | When |
|---|---|
| `query.fetch` | Every time a query fetches data |
| `mutation.execute` | Every time a mutation runs |

### Query fetch span

The `query.fetch` span is tagged with the query key and status:

| Tag | Value |
|---|---|
| `query.key` | The string representation of the `QueryKey` (e.g. `users:42`) |
| `otel.status_code` | `Ok` on success, `Error` on failure |
| `error.type` | Exception type name (only on failure) |

### Mutation execute span

The `mutation.execute` span carries the final status:

| Tag | Value |
|---|---|
| `otel.status_code` | `Ok` on success, `Error` on failure |
| `error.type` | Exception type name (only on failure) |

## Metrics

All metrics use the `"DotNetQuery"` meter name. Attach a tag filter in your metrics pipeline if needed.

| Instrument | Type | Unit | Description |
|---|---|---|---|
| `dotnetquery.fetch.duration` | Histogram | ms | Duration of each fetch operation |
| `dotnetquery.fetch.active` | UpDownCounter | — | Number of fetch operations currently in flight |
| `dotnetquery.cache.hits` | Counter | — | Cache lookups that found an existing entry |
| `dotnetquery.cache.misses` | Counter | — | Cache lookups that created a new entry |
| `dotnetquery.mutation.duration` | Histogram | ms | Duration of each mutation operation |

### Tags on metrics

| Metric | Tags |
|---|---|
| `dotnetquery.fetch.duration` | `query.key`, `status` (`success` / `failure`) |
| `dotnetquery.fetch.active` | `query.key` |
| `dotnetquery.cache.hits` | `query.key` |
| `dotnetquery.cache.misses` | `query.key` |
| `dotnetquery.mutation.duration` | `status` (`success` / `failure`) |

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
| Debug | `Mutation started` |
| Debug | `Mutation succeeded in {Duration}ms` |
| Warning | `Mutation failed after {Duration}ms` (+ exception) |
| Debug | `Mutation cancelled` |

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

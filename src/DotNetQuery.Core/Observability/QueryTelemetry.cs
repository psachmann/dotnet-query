namespace DotNetQuery.Core.Observability;

/// <summary>
/// Provides the <see cref="System.Diagnostics.ActivitySource"/> and <see cref="System.Diagnostics.Metrics.Meter"/>
/// used by DotNet Query for distributed tracing and metrics.
/// </summary>
/// <remarks>
/// No OpenTelemetry package is required in the library itself. Configure collection on the consumer side:
/// <code>
/// // Traces
/// builder.Services.AddOpenTelemetry()
///     .WithTracing(b => b.AddSource(QueryTelemetry.SourceName));
///
/// // Metrics
/// builder.Services.AddOpenTelemetry()
///     .WithMetrics(b => b.AddMeter(QueryTelemetry.SourceName));
/// </code>
/// </remarks>
public static class QueryTelemetry
{
    /// <summary>The name used for both the <see cref="ActivitySource"/> and the <see cref="Meter"/>.</summary>
    public const string SourceName = "DotNetQuery";

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivitySource"/> that emits spans for query fetches and mutation executions.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(SourceName);

    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> that emits fetch durations, cache hits/misses,
    /// retry counts, and mutation durations.
    /// </summary>
    public static readonly Meter Meter = new(SourceName);
}

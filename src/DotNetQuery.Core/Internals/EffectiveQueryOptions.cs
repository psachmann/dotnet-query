namespace DotNetQuery.Core.Internals;

internal sealed record EffectiveQueryOptions<TArgs, TData>
    where TData : class
{
    public required Func<TArgs, CancellationToken, Task<TData>> Fetcher { get; init; }

    public required TimeSpan StaleTime { get; init; }

    public required TimeSpan CacheTime { get; init; }

    public required IRetryHandler RetryHandler { get; init; }

    public required bool IsEnabled { get; init; }

    public required TimeSpan? RefetchInterval { get; init; }

    public required IEqualityComparer<TData> DataComparer { get; init; }

    public required TData? InitialData { get; init; }

    public required string? Name { get; init; }

    /// <summary>Set for streamed queries only; <c>null</c> means <see cref="Fetcher"/> is used.</summary>
    public StreamedFetch<TArgs, TData>? Stream { get; init; }
}

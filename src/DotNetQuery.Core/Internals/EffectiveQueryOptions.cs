namespace DotNetQuery.Core.Internals;

internal record EffectiveQueryOptions<TArgs, TData>
{
    public required Func<TArgs, CancellationToken, Task<TData>> Fetcher { get; init; }

    public required TimeSpan StaleTime { get; init; }

    public required TimeSpan CacheTime { get; init; }

    public required IRetryHandler RetryHandler { get; init; }

    public required bool IsEnabled { get; init; }

    public required TimeSpan? RefetchInterval { get; init; }

    public required IEqualityComparer<TData> DataComparer { get; init; }
}

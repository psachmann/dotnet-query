namespace DotNetQuery.Core.Internals;

internal sealed record EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam>
    where TData : class
{
    public required Func<TArgs, TPageParam, CancellationToken, Task<TData>> Fetcher { get; init; }

    public required TPageParam InitialPageParam { get; init; }

    public required Func<InfinitePageInfo<TData, TPageParam>, PageParam<TPageParam>> GetNextPageParam { get; init; }

    public required Func<
        InfinitePageInfo<TData, TPageParam>,
        PageParam<TPageParam>
    >? GetPreviousPageParam { get; init; }

    public required int? MaxPages { get; init; }

    public required TimeSpan StaleTime { get; init; }

    public required TimeSpan CacheTime { get; init; }

    public required IRetryHandler RetryHandler { get; init; }

    public required bool IsEnabled { get; init; }

    public required TimeSpan? RefetchInterval { get; init; }

    public required IEqualityComparer<TData> DataComparer { get; init; }

    public required TData? InitialData { get; init; }

    public required string? Name { get; init; }
}

namespace DotNetQuery.Core.Internals;

internal record EffectiveQueryOptions<TArgs, TData>
{
    public required Func<TArgs, CancellationToken, Task<TData>> Fetcher { get; init; }

    public TimeSpan StaleTime { get; init; } = TimeSpan.Zero;

    public TimeSpan CacheTime { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan? RefetchInterval { get; init; }

    public IRetryHandler RetryHandler { get; init; } = new DefaultRetryHandler();
}

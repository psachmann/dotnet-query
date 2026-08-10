namespace DotNetQuery.Core;

/// <summary>
/// Global configuration options applied to all queries and mutations created by an <see cref="IQueryClient"/>.
/// Individual <see cref="QueryOptions{TArgs,TData}"/> and <see cref="MutationOptions{TArgs,TData}"/> can override most settings.
/// </summary>
public sealed record QueryClientOptions
{
    /// <summary>
    /// The duration after which fetched data is considered stale and eligible for re-fetching.
    /// Defaults to <see cref="TimeSpan.Zero"/>, meaning data is stale immediately after being fetched.
    /// </summary>
    public TimeSpan StaleTime { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// The duration for which data is kept in the cache after all subscribers have disposed.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan CacheTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// When set, queries automatically re-fetch at this interval while they have active subscribers.
    /// <c>null</c> disables automatic re-fetching. Must be a positive duration when set.
    /// </summary>
    public TimeSpan? RefetchInterval { get; set; }

    /// <summary>
    /// The retry handler used for all queries and mutations unless overridden per-query or per-mutation.
    /// Defaults to <see cref="DefaultRetryHandler"/>.
    /// </summary>
    public IRetryHandler RetryHandler { get; set; } = new DefaultRetryHandler();

    /// <summary>
    /// Determines how the <see cref="IQueryClient"/> is registered in the DI container.
    /// Defaults to <see cref="QueryExecutionMode.Csr"/>.
    /// </summary>
    public QueryExecutionMode ExecutionMode { get; set; } = QueryExecutionMode.Csr;

    /// <summary>
    /// When <c>true</c>, metrics are additionally tagged with the full <see cref="QueryKey"/> string
    /// (e.g. <c>"users:42"</c>) alongside the low-cardinality <c>query.name</c> tag.
    /// Defaults to <c>false</c>: query keys commonly include per-entity arguments, and tagging metrics
    /// with them produces one time series per distinct argument value, which exceeds the cardinality
    /// limits most metrics backends enforce. Traces and log messages always carry the full key regardless
    /// of this setting. Only enable this when your <see cref="QueryKey"/> values are drawn from a bounded set.
    /// </summary>
    public bool IncludeQueryKeyInMetrics { get; set; }

    /// <summary>
    /// Validates all option values, throwing <see cref="ArgumentOutOfRangeException"/> or
    /// <see cref="ArgumentNullException"/> with a descriptive message for any invalid value.
    /// Called automatically by <see cref="QueryClientFactory"/> and the DI extension.
    /// </summary>
    public void Validate()
    {
        if (StaleTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StaleTime),
                StaleTime,
                $"{nameof(StaleTime)} must not be negative."
            );
        }

        if (CacheTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheTime),
                CacheTime,
                $"{nameof(CacheTime)} must not be negative."
            );
        }

        if (RefetchInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RefetchInterval),
                RefetchInterval,
                $"{nameof(RefetchInterval)} must be a positive duration when set."
            );
        }

        if (RetryHandler is null)
        {
            throw new ArgumentNullException(nameof(RetryHandler), $"{nameof(RetryHandler)} must not be null.");
        }
    }
}

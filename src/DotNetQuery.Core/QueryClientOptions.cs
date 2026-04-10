namespace DotNetQuery.Core;

public sealed record QueryClientOptions
{
    public TimeSpan StaleTime { get; set; } = TimeSpan.Zero;

    public TimeSpan CacheTime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan? RefetchInterval { get; set; }

    public IRetryHandler RetryHandler { get; set; } = new DefaultRetryHandler();

    public QueryExecutionMode ExecutionMode { get; set; } = QueryExecutionMode.Csr;

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

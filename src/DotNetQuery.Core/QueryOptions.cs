namespace DotNetQuery.Core;

public sealed record QueryOptions<TArgs, TData>
{
    public required Func<TArgs, QueryKey> KeyFactory { get; init; }

    public required Func<TArgs, CancellationToken, Task<TData>> Fetcher { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.StaleTime"/>. <c>null</c> uses the global default.</summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.CacheTime"/>. <c>null</c> uses the global default.</summary>
    public TimeSpan? CacheTime { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RefetchInterval"/>. <c>null</c> uses the global default.</summary>
    public TimeSpan? RefetchInterval { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RetryHandler"/>. <c>null</c> uses the global default.</summary>
    public IRetryHandler? RetryHandler { get; init; }

    /// <summary>
    /// Whether the query is initially enabled. Defaults to <c>true</c>.
    /// Set to <c>false</c> to create a disabled-by-default query without needing to push to the <see cref="IQuery{TArgs,TData}.Enabled"/> observer.
    /// </summary>
    public bool IsEnabled { get; init; } = true;
}

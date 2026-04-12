namespace DotNetQuery.Core;

/// <summary>
/// Configuration options for a single query created via <see cref="IQueryClient.CreateQuery{TArgs,TData}"/>.
/// Per-query settings override the global defaults set on <see cref="QueryClientOptions"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the fetcher.</typeparam>
/// <typeparam name="TData">The type of the data returned by the fetcher.</typeparam>
public sealed record QueryOptions<TArgs, TData>
{
    /// <summary>
    /// A function that derives a <see cref="QueryKey"/> from the given args.
    /// Used to identify and share cache entries across queries with the same key.
    /// </summary>
    public required Func<TArgs, QueryKey> KeyFactory { get; init; }

    /// <summary>
    /// The async function that fetches data for the given args.
    /// Receives a <see cref="CancellationToken"/> that is cancelled when the query is disposed or superseded.
    /// </summary>
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
    /// Set to <c>false</c> to create a disabled-by-default query without needing to call <see cref="IQuery{TArgs,TData}.SetEnabled"/>.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Data used to pre-populate the query result before the first fetch completes.
    /// The query is always treated as immediately stale when initial data is present,
    /// so a background fetch begins as soon as the first subscriber joins.
    /// Has no effect if the cache already holds a real entry for the derived key.
    /// </summary>
    public TData? InitialData { get; init; }

    /// <summary>
    /// Comparer used to determine whether newly fetched data is structurally equal to the previously cached value.
    /// When equal, the cached data reference is preserved and <see cref="IQuery{TArgs,TData}.Success"/> will not re-emit.
    /// Defaults to <c>null</c>, which falls back to <see cref="EqualityComparer{T}.Default"/>
    /// (reference equality for classes, value equality for records and primitives).
    /// </summary>
    public IEqualityComparer<TData>? DataComparer { get; init; }
}

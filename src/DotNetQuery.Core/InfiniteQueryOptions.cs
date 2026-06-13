namespace DotNetQuery.Core;

/// <summary>
/// Configuration for an infinite (paginated) query created via
/// <see cref="IQueryClient.CreateInfiniteQuery{TArgs,TData,TPageParam}"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments that identify which resource to fetch.</typeparam>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
public sealed record InfiniteQueryOptions<TArgs, TData, TPageParam>
{
    /// <summary>Derives the cache key from the current args.</summary>
    public required Func<TArgs, QueryKey> KeyFactory { get; init; }

    /// <summary>Fetches a single page of data for the given args and page parameter.</summary>
    public required Func<TArgs, TPageParam, CancellationToken, Task<TData>> Fetcher { get; init; }

    /// <summary>The page parameter used for the very first page fetch.</summary>
    public required TPageParam InitialPageParam { get; init; }

    /// <summary>
    /// Derives the page parameter for the next page from the boundary info of the last loaded page.
    /// Return <see cref="PageParam{TPageParam}.None"/> to signal there are no more pages.
    /// </summary>
    public required Func<InfinitePageInfo<TData, TPageParam>, PageParam<TPageParam>> GetNextPageParam { get; init; }

    /// <summary>
    /// Optionally derives the page parameter for the previous page from the boundary info of the
    /// first loaded page. When <c>null</c>,
    /// <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchPreviousPage"/> is always a no-op.
    /// Return <see cref="PageParam{TPageParam}.None"/> to signal there are no earlier pages.
    /// </summary>
    public Func<InfinitePageInfo<TData, TPageParam>, PageParam<TPageParam>>? GetPreviousPageParam { get; init; }

    /// <summary>
    /// Maximum number of pages to keep. When exceeded during <c>FetchNextPage</c>, the oldest pages
    /// are dropped from the front; during <c>FetchPreviousPage</c>, the newest are dropped from the back.
    /// <c>null</c> means unbounded (default).
    /// </summary>
    public int? MaxPages { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.StaleTime"/> for this query.</summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.CacheTime"/> for this query.</summary>
    public TimeSpan? CacheTime { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RefetchInterval"/> for this query.</summary>
    public TimeSpan? RefetchInterval { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.RetryHandler"/> for this query.</summary>
    public IRetryHandler? RetryHandler { get; init; }

    /// <summary>
    /// When <c>false</c>, the query will not fetch until explicitly enabled.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Equality comparer applied per page to detect structurally identical pages.
    /// Defaults to <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public IEqualityComparer<TData>? DataComparer { get; init; }
}

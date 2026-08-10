namespace DotNetQuery.Core;

/// <summary>
/// Configuration for an infinite (paginated) query created via
/// <see cref="IQueryClient.CreateInfiniteQuery{TArgs,TData,TPageParam}"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments that identify which resource to fetch.</typeparam>
/// <typeparam name="TData">
/// The type of a single page of data. Constrained to reference types because the library
/// uses <c>null</c> to represent "no data yet" — a non-nullable value type has no way to express that.
/// </typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
public sealed record InfiniteQueryOptions<TArgs, TData, TPageParam>
    where TData : class
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
    /// During a full refetch, a re-fetched page that equals its predecessor keeps the previous
    /// instance (reference equality is preserved), and
    /// <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.Success"/> suppresses re-emissions when
    /// every page is equal to the previous emission.
    /// Defaults to <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public IEqualityComparer<TData>? DataComparer { get; init; }

    /// <summary>
    /// Pre-seeds the first page so the query starts in a <c>Success</c> state without an initial
    /// network fetch. The seeded page is immediately displayed while a background fetch refreshes
    /// it. Only the first page is seeded; subsequent pages require
    /// <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchNextPage"/>.
    /// </summary>
    public TData? InitialData { get; init; }

    /// <summary>
    /// A short, low-cardinality name used to tag metrics for this query (e.g. <c>"users"</c>, <c>"todos"</c>).
    /// Metrics are never tagged with the full <see cref="QueryKey"/> — which typically includes per-entity
    /// arguments such as an id — because that would produce one time series per distinct argument value.
    /// When <c>null</c>, the first part of the derived <see cref="QueryKey"/> is used instead.
    /// Traces and log messages always carry the full key regardless of this setting.
    /// See <see cref="QueryClientOptions.IncludeQueryKeyInMetrics"/> to opt back into per-key metrics.
    /// </summary>
    public string? Name { get; init; }
}

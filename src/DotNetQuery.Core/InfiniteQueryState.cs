namespace DotNetQuery.Core;

/// <summary>
/// An immutable snapshot of an infinite query's current state.
/// Use the static factory methods to create instances.
/// </summary>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
public sealed record InfiniteQueryState<TData, TPageParam>
{
    /// <summary>The current lifecycle status of the query.</summary>
    public QueryStatus Status { get; private set; }

    /// <summary>All currently loaded pages, ordered from oldest to newest.</summary>
    public IReadOnlyList<TData> Pages { get; private set; } = [];

    /// <summary>
    /// The page parameters used to fetch each page in <see cref="Pages"/>.
    /// <c>PageParams[i]</c> was the parameter used to fetch <c>Pages[i]</c>.
    /// </summary>
    public IReadOnlyList<TPageParam> PageParams { get; private set; } = [];

    /// <summary>The exception from the most recent failed fetch. <c>null</c> when not in a failure state.</summary>
    public Exception? Error { get; private set; }

    /// <summary><c>true</c> when a next page is available to fetch.</summary>
    public bool HasNextPage { get; private set; }

    /// <summary><c>true</c> when a previous page is available to fetch.</summary>
    public bool HasPreviousPage { get; private set; }

    /// <summary>
    /// <c>true</c> while a <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchNextPage"/> is in progress.
    /// Existing pages remain visible during this state.
    /// </summary>
    public bool IsFetchingNextPage { get; private set; }

    /// <summary>
    /// <c>true</c> while a <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchPreviousPage"/> is in progress.
    /// Existing pages remain visible during this state.
    /// </summary>
    public bool IsFetchingPreviousPage { get; private set; }

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Idle"/>.</summary>
    public bool IsIdle => Status == QueryStatus.Idle;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Fetching"/>.</summary>
    public bool IsFetching => Status == QueryStatus.Fetching;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Success"/>.</summary>
    public bool IsSuccess => Status == QueryStatus.Success;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Failure"/>.</summary>
    public bool IsFailure => Status == QueryStatus.Failure;

    /// <summary><c>true</c> when at least one page has been successfully loaded.</summary>
    public bool HasData => Pages.Count > 0;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => Error is not null;

    /// <summary>Creates an <see cref="QueryStatus.Idle"/> state with no pages.</summary>
    public static InfiniteQueryState<TData, TPageParam> CreateIdle() => new() { Status = QueryStatus.Idle };

    /// <summary>
    /// Creates a <see cref="QueryStatus.Fetching"/> state that carries existing pages for
    /// stale-while-revalidate display during a full refetch.
    /// </summary>
    public static InfiniteQueryState<TData, TPageParam> CreateFetching(
        IReadOnlyList<TData> pages,
        IReadOnlyList<TPageParam> pageParams,
        bool hasNextPage,
        bool hasPreviousPage
    ) =>
        new()
        {
            Status = QueryStatus.Fetching,
            Pages = pages,
            PageParams = pageParams,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
        };

    /// <summary>
    /// Creates a success state with <see cref="IsFetchingNextPage"/> set.
    /// Existing pages remain visible while the next page loads.
    /// </summary>
    public static InfiniteQueryState<TData, TPageParam> CreateFetchingNext(
        IReadOnlyList<TData> pages,
        IReadOnlyList<TPageParam> pageParams,
        bool hasNextPage,
        bool hasPreviousPage
    ) =>
        new()
        {
            Status = QueryStatus.Success,
            Pages = pages,
            PageParams = pageParams,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
            IsFetchingNextPage = true,
        };

    /// <summary>
    /// Creates a success state with <see cref="IsFetchingPreviousPage"/> set.
    /// Existing pages remain visible while the previous page loads.
    /// </summary>
    public static InfiniteQueryState<TData, TPageParam> CreateFetchingPrevious(
        IReadOnlyList<TData> pages,
        IReadOnlyList<TPageParam> pageParams,
        bool hasNextPage,
        bool hasPreviousPage
    ) =>
        new()
        {
            Status = QueryStatus.Success,
            Pages = pages,
            PageParams = pageParams,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
            IsFetchingPreviousPage = true,
        };

    /// <summary>Creates a settled <see cref="QueryStatus.Success"/> state with the given pages.</summary>
    public static InfiniteQueryState<TData, TPageParam> CreateSuccess(
        IReadOnlyList<TData> pages,
        IReadOnlyList<TPageParam> pageParams,
        bool hasNextPage,
        bool hasPreviousPage
    ) =>
        new()
        {
            Status = QueryStatus.Success,
            Pages = pages,
            PageParams = pageParams,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
        };

    /// <summary>Creates a <see cref="QueryStatus.Failure"/> state, preserving pages from the last successful fetch.</summary>
    public static InfiniteQueryState<TData, TPageParam> CreateFailure(
        Exception error,
        IReadOnlyList<TData> pages,
        IReadOnlyList<TPageParam> pageParams,
        bool hasNextPage,
        bool hasPreviousPage
    ) =>
        new()
        {
            Status = QueryStatus.Failure,
            Error = error,
            Pages = pages,
            PageParams = pageParams,
            HasNextPage = hasNextPage,
            HasPreviousPage = hasPreviousPage,
        };
}

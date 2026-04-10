namespace DotNetQuery.Core;

/// <summary>
/// An immutable snapshot of a query's current state. Use the static factory methods to create instances.
/// </summary>
/// <typeparam name="TData">The type of data returned by the query.</typeparam>
public sealed record QueryState<TData>
{
    /// <summary>The current lifecycle status of the query.</summary>
    public QueryStatus Status { get; private set; }

    /// <summary>
    /// The data returned by the most recent successful fetch.
    /// <c>null</c> when the query has not yet succeeded or has no data.
    /// </summary>
    public TData? CurrentData { get; private set; }

    /// <summary>
    /// The data from the previous successful fetch, carried forward across subsequent fetches and failures.
    /// Useful for rendering stale data while a new fetch is in progress.
    /// </summary>
    public TData? LastData { get; private set; }

    /// <summary>The exception from the most recent failed fetch. <c>null</c> when not in a failure state.</summary>
    public Exception? Error { get; private set; }

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Idle"/>.</summary>
    public bool IsIdle => Status == QueryStatus.Idle;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Fetching"/>.</summary>
    public bool IsFetching => Status == QueryStatus.Fetching;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Success"/>.</summary>
    public bool IsSuccess => Status == QueryStatus.Success;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="QueryStatus.Failure"/>.</summary>
    public bool IsFailure => Status == QueryStatus.Failure;

    /// <summary><c>true</c> when <see cref="CurrentData"/> is not <c>null</c>.</summary>
    public bool HasData => CurrentData is not null;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => Error is not null;

    /// <summary>Creates an <see cref="QueryStatus.Idle"/> state, optionally carrying forward <paramref name="lastData"/>.</summary>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    public static QueryState<TData> CreateIdle(TData? lastData = default) =>
        new() { Status = QueryStatus.Idle, LastData = lastData };

    /// <summary>Creates a <see cref="QueryStatus.Fetching"/> state, optionally carrying forward <paramref name="lastData"/>.</summary>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    public static QueryState<TData> CreateFetching(TData? lastData = default) =>
        new() { Status = QueryStatus.Fetching, LastData = lastData };

    /// <summary>Creates a <see cref="QueryStatus.Success"/> state with the fetched data.</summary>
    /// <param name="currentData">The data returned by the fetch.</param>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    public static QueryState<TData> CreateSuccess(TData currentData, TData? lastData = default) =>
        new()
        {
            Status = QueryStatus.Success,
            CurrentData = currentData,
            LastData = lastData,
        };

    /// <summary>Creates a <see cref="QueryStatus.Failure"/> state with the given error.</summary>
    /// <param name="error">The exception thrown by the fetch.</param>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    public static QueryState<TData> CreateFailure(Exception error, TData? lastData = default) =>
        new()
        {
            Status = QueryStatus.Failure,
            LastData = lastData,
            Error = error,
        };
}

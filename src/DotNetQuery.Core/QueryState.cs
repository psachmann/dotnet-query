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
    /// Only meaningful when <see cref="HasData"/> is <c>true</c> — for a non-nullable value type
    /// <typeparamref name="TData"/>, <c>default</c> is itself a valid fetched value.
    /// </summary>
    public TData? CurrentData { get; private set; }

    /// <summary>
    /// The data from the previous successful fetch, carried forward across subsequent fetches and failures.
    /// Only meaningful when <see cref="HasLastData"/> is <c>true</c>. Useful for rendering stale data while
    /// a new fetch is in progress.
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

    /// <summary>
    /// <c>true</c> when a successful fetch has produced <see cref="CurrentData"/>. The reliable presence
    /// check for any <typeparamref name="TData"/>, including non-nullable value types where <c>default</c>
    /// is itself a valid value and can't be used to infer presence.
    /// </summary>
    public bool HasData { get; private set; }

    /// <summary><c>true</c> when <see cref="LastData"/> holds a value carried forward from a previous successful fetch.</summary>
    public bool HasLastData { get; private set; }

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => Error is not null;

    /// <summary>Creates an <see cref="QueryStatus.Idle"/> state, optionally carrying forward <paramref name="lastData"/>.</summary>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    /// <param name="hasLastData">
    /// <c>true</c> when <paramref name="lastData"/> is an actual carried-forward value. Defaults to inferring
    /// presence from <c>lastData is not null</c>; pass explicitly for a non-nullable value type
    /// <typeparamref name="TData"/>, where <c>default</c> is itself a valid carried-forward value.
    /// </param>
    public static QueryState<TData> CreateIdle(TData? lastData = default, bool? hasLastData = null) =>
        new()
        {
            Status = QueryStatus.Idle,
            LastData = lastData,
            HasLastData = hasLastData ?? lastData is not null,
        };

    /// <summary>Creates a <see cref="QueryStatus.Fetching"/> state, optionally carrying forward <paramref name="lastData"/>.</summary>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    /// <param name="hasLastData">
    /// <c>true</c> when <paramref name="lastData"/> is an actual carried-forward value. Defaults to inferring
    /// presence from <c>lastData is not null</c>; pass explicitly for a non-nullable value type
    /// <typeparamref name="TData"/>, where <c>default</c> is itself a valid carried-forward value.
    /// </param>
    public static QueryState<TData> CreateFetching(TData? lastData = default, bool? hasLastData = null) =>
        new()
        {
            Status = QueryStatus.Fetching,
            LastData = lastData,
            HasLastData = hasLastData ?? lastData is not null,
        };

    /// <summary>Creates a <see cref="QueryStatus.Success"/> state with the fetched data.</summary>
    /// <param name="currentData">The data returned by the fetch.</param>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    /// <param name="hasLastData">
    /// <c>true</c> when <paramref name="lastData"/> is an actual carried-forward value. Defaults to inferring
    /// presence from <c>lastData is not null</c>; pass explicitly for a non-nullable value type
    /// <typeparamref name="TData"/>, where <c>default</c> is itself a valid carried-forward value.
    /// </param>
    public static QueryState<TData> CreateSuccess(
        TData currentData,
        TData? lastData = default,
        bool? hasLastData = null
    ) =>
        new()
        {
            Status = QueryStatus.Success,
            CurrentData = currentData,
            HasData = true,
            LastData = lastData,
            HasLastData = hasLastData ?? lastData is not null,
        };

    /// <summary>Creates a <see cref="QueryStatus.Failure"/> state with the given error.</summary>
    /// <param name="error">The exception thrown by the fetch.</param>
    /// <param name="lastData">Data from the previous successful fetch to carry forward.</param>
    /// <param name="hasLastData">
    /// <c>true</c> when <paramref name="lastData"/> is an actual carried-forward value. Defaults to inferring
    /// presence from <c>lastData is not null</c>; pass explicitly for a non-nullable value type
    /// <typeparamref name="TData"/>, where <c>default</c> is itself a valid carried-forward value.
    /// </param>
    public static QueryState<TData> CreateFailure(
        Exception error,
        TData? lastData = default,
        bool? hasLastData = null
    ) =>
        new()
        {
            Status = QueryStatus.Failure,
            LastData = lastData,
            HasLastData = hasLastData ?? lastData is not null,
            Error = error,
        };
}

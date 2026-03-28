namespace DotNetQuery.Core;

public sealed record QueryState<TData>
{
    public QueryStatus Status { get; private set; }

    public TData? CurrentData { get; private set; }

    public TData? LastData { get; private set; }

    public Exception? Error { get; private set; }

    public bool IsIdle => Status == QueryStatus.Idle;

    public bool IsFetching => Status == QueryStatus.Fetching;

    public bool IsSuccess => Status == QueryStatus.Success;

    public bool IsFailure => Status == QueryStatus.Failure;

    public bool HasData => CurrentData is not null;

    public bool HasError => Error is not null;

    public static QueryState<TData> CreateIdle(TData? lastData = default) =>
        new() { Status = QueryStatus.Idle, LastData = lastData };

    public static QueryState<TData> CreateFetching(TData? lastData = default) =>
        new() { Status = QueryStatus.Fetching, LastData = lastData };

    public static QueryState<TData> CreateSuccess(TData currentData, TData? lastData = default) =>
        new()
        {
            Status = QueryStatus.Success,
            CurrentData = currentData,
            LastData = lastData,
        };

    public static QueryState<TData> CreateFailure(Exception error, TData? lastData = default) =>
        new()
        {
            Status = QueryStatus.Failure,
            LastData = lastData,
            Error = error,
        };
}

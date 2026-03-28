namespace DotNetQuery.Core;

public sealed record MutationState<TData>
{
    public MutationStatus Status { get; private set; }

    public TData? Data { get; private set; }

    public Exception? Error { get; private set; }

    public bool IsIdle => Status == MutationStatus.Idle;

    public bool IsRunning => Status == MutationStatus.Running;

    public bool IsSuccess => Status == MutationStatus.Success;

    public bool IsFailure => Status == MutationStatus.Failure;

    public bool HasData => IsSuccess;

    public bool HasError => Error is not null;

    public static MutationState<TData> CreateIdle() => new() { Status = MutationStatus.Idle };

    public static MutationState<TData> CreateRunning() => new() { Status = MutationStatus.Running };

    public static MutationState<TData> CreateSuccess(TData data) =>
        new() { Status = MutationStatus.Success, Data = data };

    public static MutationState<TData> CreateFailure(Exception error) =>
        new() { Status = MutationStatus.Failure, Error = error };
}

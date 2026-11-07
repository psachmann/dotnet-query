namespace DotNetQuery.Core;

/// <summary>
/// An immutable snapshot of a mutation's current state. Use the static factory methods to create instances.
/// </summary>
/// <typeparam name="TData">The type of data returned by the mutation on success.</typeparam>
public sealed record MutationState<TData> : IMutationState<TData>
{
    /// <summary>The current lifecycle status of the mutation.</summary>
    public MutationStatus Status { get; private set; }

    /// <summary>
    /// The data returned by the most recent successful execution.
    /// <c>null</c> when the mutation has not yet succeeded.
    /// </summary>
    public TData? CurrentData { get; private set; }

    /// <summary>The exception from the most recent failed execution. <c>null</c> when not in a failure state.</summary>
    public Exception? Error { get; private set; }

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Idle"/>.</summary>
    public bool IsIdle => Status == MutationStatus.Idle;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Running"/>.</summary>
    public bool IsRunning => Status == MutationStatus.Running;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Success"/>.</summary>
    public bool IsSuccess => Status == MutationStatus.Success;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Failure"/>.</summary>
    public bool IsFailure => Status == MutationStatus.Failure;

    /// <summary><c>true</c> when the mutation has succeeded and <see cref="CurrentData"/> is available.</summary>
    public bool HasData => IsSuccess;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => Error is not null;

    /// <summary>Creates an <see cref="MutationStatus.Idle"/> state.</summary>
    public static MutationState<TData> CreateIdle() => new() { Status = MutationStatus.Idle };

    /// <summary>Creates a <see cref="MutationStatus.Running"/> state.</summary>
    public static MutationState<TData> CreateRunning() => new() { Status = MutationStatus.Running };

    /// <summary>Creates a <see cref="MutationStatus.Success"/> state with the returned data.</summary>
    /// <param name="data">The data returned by the mutation.</param>
    public static MutationState<TData> CreateSuccess(TData data) =>
        new() { Status = MutationStatus.Success, CurrentData = data };

    /// <summary>Creates a <see cref="MutationStatus.Failure"/> state with the given error.</summary>
    /// <param name="error">The exception thrown by the mutation.</param>
    public static MutationState<TData> CreateFailure(Exception error) =>
        new() { Status = MutationStatus.Failure, Error = error };
}

/// <summary>
/// An immutable snapshot of a progress-reporting mutation's current state. Identical to
/// <see cref="MutationState{TData}"/> plus <see cref="Progress"/>, the latest value the mutator reported.
/// Use the static factory methods to create instances.
/// </summary>
/// <typeparam name="TData">The type of data returned by the mutation on success.</typeparam>
/// <typeparam name="TProgress">The type of the progress values the mutator reports.</typeparam>
public sealed record MutationState<TData, TProgress> : IMutationState<TData>
{
    /// <summary>The current lifecycle status of the mutation.</summary>
    public MutationStatus Status { get; private set; }

    /// <summary>
    /// The data returned by the most recent successful execution.
    /// <c>null</c> when the mutation has not yet succeeded.
    /// </summary>
    public TData? CurrentData { get; private set; }

    /// <summary>The exception from the most recent failed execution. <c>null</c> when not in a failure state.</summary>
    public Exception? Error { get; private set; }

    /// <summary>
    /// The latest value the mutator reported through its <see cref="IProgress{T}"/>.
    /// <c>default</c> before the first report of an attempt — it resets at the start of every retry,
    /// because a retried mutator starts over. <see cref="MutationStatus.Success"/> and
    /// <see cref="MutationStatus.Failure"/> keep the last reported value (so a UI can show
    /// "failed at 60 %"); <see cref="MutationStatus.Idle"/> — including after a cancellation — clears it.
    /// </summary>
    public TProgress? Progress { get; private set; }

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Idle"/>.</summary>
    public bool IsIdle => Status == MutationStatus.Idle;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Running"/>.</summary>
    public bool IsRunning => Status == MutationStatus.Running;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Success"/>.</summary>
    public bool IsSuccess => Status == MutationStatus.Success;

    /// <summary><c>true</c> when <see cref="Status"/> is <see cref="MutationStatus.Failure"/>.</summary>
    public bool IsFailure => Status == MutationStatus.Failure;

    /// <summary><c>true</c> when the mutation has succeeded and <see cref="CurrentData"/> is available.</summary>
    public bool HasData => IsSuccess;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => Error is not null;

    /// <summary>Creates an <see cref="MutationStatus.Idle"/> state with no progress.</summary>
    public static MutationState<TData, TProgress> CreateIdle() => new() { Status = MutationStatus.Idle };

    /// <summary>Creates a <see cref="MutationStatus.Running"/> state carrying the latest reported progress.</summary>
    /// <param name="progress">The latest reported progress; <c>default</c> before the first report.</param>
    public static MutationState<TData, TProgress> CreateRunning(TProgress? progress) =>
        new() { Status = MutationStatus.Running, Progress = progress };

    /// <summary>Creates a <see cref="MutationStatus.Success"/> state with the returned data.</summary>
    /// <param name="data">The data returned by the mutation.</param>
    /// <param name="progress">The last progress the mutator reported before it returned.</param>
    public static MutationState<TData, TProgress> CreateSuccess(TData data, TProgress? progress) =>
        new()
        {
            Status = MutationStatus.Success,
            CurrentData = data,
            Progress = progress,
        };

    /// <summary>Creates a <see cref="MutationStatus.Failure"/> state with the given error.</summary>
    /// <param name="error">The exception thrown by the mutation.</param>
    /// <param name="progress">The last progress the mutator reported before it threw.</param>
    public static MutationState<TData, TProgress> CreateFailure(Exception error, TProgress? progress) =>
        new()
        {
            Status = MutationStatus.Failure,
            Error = error,
            Progress = progress,
        };
}

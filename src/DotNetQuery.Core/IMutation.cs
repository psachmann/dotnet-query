namespace DotNetQuery.Core;

/// <summary>
/// Represents a mutation that executes a side-effecting operation with <typeparamref name="TArgs"/>
/// and produces <typeparamref name="TData"/> as a result.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the mutation.</typeparam>
/// <typeparam name="TData">The type of the data returned on success.</typeparam>
public interface IMutation<TArgs, TData> : IDisposable
{
    /// <summary>
    /// The current state snapshot. Can be read synchronously without subscribing — in particular,
    /// this is updated to <see cref="MutationStatus.Running"/> before <see cref="Execute"/> returns,
    /// so callers can check it to avoid double-submitting while a dispatched UI update is in flight.
    /// </summary>
    public MutationState<TData> CurrentState { get; }

    /// <summary>
    /// Enables or disables the mutation. When <c>false</c>, calls to <see cref="Execute"/> are silently
    /// ignored. Pass <c>true</c> to allow subsequent executions.
    /// </summary>
    public void SetEnabled(bool enabled);

    /// <summary>
    /// Emits on every state transition (e.g. Idle → Executing → Success/Failed).
    /// Replays the latest state to new subscribers.
    /// </summary>
    public IObservable<MutationState<TData>> State { get; }

    /// <summary>
    /// Emits the unwrapped <typeparamref name="TData"/> on each successful execution.
    /// Derived from <see cref="State"/>, so if the current state is already a success, a new
    /// subscriber receives that value immediately rather than waiting for the next execution.
    /// </summary>
    public IObservable<TData> Success { get; }

    /// <summary>
    /// Emits the <see cref="Exception"/> on each failed execution.
    /// Derived from <see cref="State"/>, so if the current state is already a failure, a new
    /// subscriber receives that exception immediately rather than waiting for the next execution.
    /// </summary>
    public IObservable<Exception> Failure { get; }

    /// <summary>
    /// Emits the final <see cref="MutationState{TData}"/> after each execution completes,
    /// regardless of whether it succeeded or failed.
    /// </summary>
    public IObservable<MutationState<TData>> Settled { get; }

    /// <summary>
    /// Triggers the mutation with the given <paramref name="args"/>.
    /// If the mutation is disabled, the call is silently ignored.
    /// </summary>
    /// <param name="args">The arguments to pass to the mutation handler.</param>
    public void Execute(TArgs args);

    /// <summary>
    /// Cancels the currently running execution, if any.
    /// </summary>
    public void Cancel();
}

/// <summary>
/// A mutation whose mutator reports progress while it runs — an upload, export, import or other
/// long-running job. Behaves exactly like <see cref="IMutation{TArgs,TData}"/>; the only addition is
/// that every state carries the latest reported <see cref="MutationState{TData,TProgress}.Progress"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the mutation.</typeparam>
/// <typeparam name="TData">The type of the data returned on success.</typeparam>
/// <typeparam name="TProgress">The type of the progress values the mutator reports.</typeparam>
public interface IMutation<TArgs, TData, TProgress> : IDisposable
{
    /// <summary>
    /// The current state snapshot. Can be read synchronously without subscribing — in particular,
    /// this is updated to <see cref="MutationStatus.Running"/> before <see cref="Execute"/> returns,
    /// so callers can check it to avoid double-submitting while a dispatched UI update is in flight.
    /// </summary>
    public MutationState<TData, TProgress> CurrentState { get; }

    /// <summary>
    /// Enables or disables the mutation. When <c>false</c>, calls to <see cref="Execute"/> are silently
    /// ignored. Pass <c>true</c> to allow subsequent executions.
    /// </summary>
    public void SetEnabled(bool enabled);

    /// <summary>
    /// Emits on every state transition (e.g. Idle → Running → Success/Failed) <b>and on every progress
    /// report</b>, as a <see cref="MutationStatus.Running"/> state carrying the new
    /// <see cref="MutationState{TData,TProgress}.Progress"/>. Progress travels on this stream rather than
    /// a separate one so status and progress are always consistent with each other, and a subscriber
    /// joining mid-run renders the current progress straight away.
    /// Replays the latest state to new subscribers.
    /// </summary>
    public IObservable<MutationState<TData, TProgress>> State { get; }

    /// <summary>
    /// Emits the unwrapped <typeparamref name="TData"/> on each successful execution.
    /// Derived from <see cref="State"/>, so if the current state is already a success, a new
    /// subscriber receives that value immediately rather than waiting for the next execution.
    /// </summary>
    public IObservable<TData> Success { get; }

    /// <summary>
    /// Emits the <see cref="Exception"/> on each failed execution.
    /// Derived from <see cref="State"/>, so if the current state is already a failure, a new
    /// subscriber receives that exception immediately rather than waiting for the next execution.
    /// </summary>
    public IObservable<Exception> Failure { get; }

    /// <summary>
    /// Emits the final <see cref="MutationState{TData,TProgress}"/> after each execution completes,
    /// regardless of whether it succeeded or failed.
    /// </summary>
    public IObservable<MutationState<TData, TProgress>> Settled { get; }

    /// <summary>
    /// Triggers the mutation with the given <paramref name="args"/>.
    /// If the mutation is disabled, the call is silently ignored.
    /// </summary>
    /// <param name="args">The arguments to pass to the mutation handler.</param>
    public void Execute(TArgs args);

    /// <summary>
    /// Cancels the currently running execution, if any. The mutation returns to
    /// <see cref="MutationStatus.Idle"/> and its progress is cleared.
    /// </summary>
    public void Cancel();
}

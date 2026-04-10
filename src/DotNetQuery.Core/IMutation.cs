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
    /// </summary>
    public IObservable<TData> Success { get; }

    /// <summary>
    /// Emits the <see cref="Exception"/> on each failed execution.
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

namespace DotNetQuery.Core;

public interface IMutation<TArgs, TData> : IDisposable
{
    /// <summary>
    /// Enables or disables the mutation. When <c>false</c>, calls to <see cref="Execute"/> are silently
    /// ignored. Pass <c>true</c> to allow subsequent executions.
    /// </summary>
    public void SetEnabled(bool enabled);

    public IObservable<MutationState<TData>> State { get; }

    public IObservable<TData> Success { get; }

    public IObservable<Exception> Failure { get; }

    public IObservable<MutationState<TData>> Settled { get; }

    public void Execute(TArgs args);

    public void Cancel();
}

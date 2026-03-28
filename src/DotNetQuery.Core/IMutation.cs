namespace DotNetQuery.Core;

public interface IMutation<TArgs, TData> : IDisposable
{
    public IObserver<bool> IsEnabled { get; }

    public IObservable<MutationState<TData>> State { get; }

    public IObservable<TData> Success { get; }

    public IObservable<Exception> Failure { get; }

    public IObservable<MutationState<TData>> Settled { get; }

    public void Execute(TArgs args);

    public void Cancel();
}

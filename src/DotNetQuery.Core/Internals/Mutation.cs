namespace DotNetQuery.Core.Internals;

internal sealed class Mutation<TArgs, TData> : IMutation<TArgs, TData>
{
    private readonly MutationOptions<TArgs, TData> _options;
    private readonly BehaviorSubject<MutationState<TData>> _state = new(MutationState<TData>.CreateIdle());
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _execute = new();
    private readonly Subject<Unit> _cancel = new();
    private readonly IDisposable _pipelineSubscription;
    private readonly List<IDisposable> _disposables = [];
    private bool _disposed;

    public Mutation(MutationOptions<TArgs, TData> options)
    {
        _options = options;
        _isEnabled = new(options.IsEnabled);

        _pipelineSubscription = _execute
            .Select(args => Observable.FromAsync(ct => ExecuteAsync(args, ct)).TakeUntil(_cancel))
            .Switch()
            .Subscribe();
    }

    public IObserver<bool> IsEnabled => _isEnabled.AsObserver();

    public IObservable<MutationState<TData>> State => _state.AsObservable();

    public IObservable<TData> Success => _state.Where(state => state.IsSuccess).Select(state => state.Data!);

    public IObservable<Exception> Failure => _state.Where(state => state.IsFailure).Select(state => state.Error!);

    public IObservable<MutationState<TData>> Settled => _state.Where(state => state.IsSuccess || state.IsFailure);

    public void Execute(TArgs args)
    {
        if (!_isEnabled.Value)
        {
            return;
        }

        _execute.OnNext(args);
    }

    public void Cancel() => _cancel.OnNext(Unit.Default);

    internal void AddDisposable(IDisposable disposable) => _disposables.Add(disposable);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pipelineSubscription.Dispose();
        _execute.OnCompleted();
        _cancel.OnCompleted();
        _isEnabled.OnCompleted();
        _state.OnCompleted();
        _execute.Dispose();
        _cancel.Dispose();
        _isEnabled.Dispose();
        _state.Dispose();

        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    private async Task ExecuteAsync(TArgs args, CancellationToken cancellationToken)
    {
        _state.OnNext(MutationState<TData>.CreateRunning());

        try
        {
            var data = await _options.RetryHandler.ExecuteAsync(ct => _options.Mutator(args, ct), cancellationToken);

            _state.OnNext(MutationState<TData>.CreateSuccess(data));
            _options.OnSuccess?.Invoke(args, data);
            _options.OnSettled?.Invoke();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _state.OnNext(MutationState<TData>.CreateIdle());
        }
        catch (Exception error)
        {
            _state.OnNext(MutationState<TData>.CreateFailure(error));
            _options.OnFailure?.Invoke(error);
            _options.OnSettled?.Invoke();
        }
    }
}

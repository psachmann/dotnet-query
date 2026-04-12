namespace DotNetQuery.Core.Internals;

internal sealed class Mutation<TArgs, TData> : IMutation<TArgs, TData>
{
    private readonly EffectiveMutationOptions<TArgs, TData> _options;
    private readonly QueryInstrumentation _instrumentation;
    private readonly BehaviorSubject<MutationState<TData>> _state = new(MutationState<TData>.CreateIdle());
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _execute = new();
    private readonly Subject<Unit> _cancel = new();
    private readonly CompositeDisposable _subscriptions = [];
    private bool _disposed;

    public Mutation(
        MutationOptions<TArgs, TData> options,
        QueryClientOptions globalOptions,
        QueryInstrumentation instrumentation
    )
    {
        _options = MergeOptions(options, globalOptions);
        _instrumentation = instrumentation;
        _isEnabled = new(_options.IsEnabled);

        _subscriptions.Add(
            _execute
                .Select(args => Observable.FromAsync(ct => ExecuteAsync(args, ct)).TakeUntil(_cancel))
                .Switch()
                .Subscribe()
        );
    }

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public IObservable<MutationState<TData>> State => _state.AsObservable();

    public IObservable<TData> Success => _state.Where(state => state.IsSuccess).Select(state => state.CurrentData!);

    public IObservable<Exception> Failure => _state.Where(state => state.IsFailure).Select(state => state.Error!);

    public IObservable<MutationState<TData>> Settled => _state.Where(state => state.IsSuccess || state.IsFailure);

    public static EffectiveMutationOptions<TArgs, TData> MergeOptions(
        MutationOptions<TArgs, TData> options,
        QueryClientOptions globalOptions
    )
    {
        return new()
        {
            Mutator = options.Mutator,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            IsEnabled = options.IsEnabled,
            InvalidateKeys = options.InvalidateKeys ?? [],
            OnMutate = options.OnMutate,
            OnSuccess = options.OnSuccess ?? delegate { },
            OnFailure = options.OnFailure ?? delegate { },
            OnSettled = options.OnSettled ?? delegate { },
        };
    }

    public void Execute(TArgs args)
    {
        if (!_isEnabled.Value)
        {
            return;
        }

        _execute.OnNext(args);
    }

    public void Cancel() => _cancel.OnNext(Unit.Default);

    internal void AddDisposable(IDisposable disposable) => _subscriptions.Add(disposable);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        _execute.OnCompleted();
        _cancel.OnCompleted();
        _isEnabled.OnCompleted();
        _state.OnCompleted();
        _execute.Dispose();
        _cancel.Dispose();
        _isEnabled.Dispose();
        _state.Dispose();
    }

    private async Task ExecuteAsync(TArgs args, CancellationToken cancellationToken)
    {
        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityMutationExecute);
        var stopwatch = Stopwatch.StartNew();

        var rollback = _options.OnMutate?.Invoke(args);

        _state.OnNext(MutationState<TData>.CreateRunning());
        _instrumentation.RecordMutationStart();

        TData data = default!;
        Exception? error = null;
        var cancelled = false;

        try
        {
            data = await _options.RetryHandler.ExecuteAsync(ct => _options.Mutator(args, ct), cancellationToken);
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordMutationSuccess(stopwatch.Elapsed.TotalMilliseconds);

            if (!_disposed)
            {
                _state.OnNext(MutationState<TData>.CreateSuccess(data));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            cancelled = true;
            rollback?.Invoke();

            _instrumentation.RecordMutationCancelled();

            if (!_disposed)
            {
                _state.OnNext(MutationState<TData>.CreateIdle());
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            error = ex;
            rollback?.Invoke();

            activity?.SetTag(QueryTelemetryTags.TagErrorType, ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _instrumentation.RecordMutationFailure(stopwatch.Elapsed.TotalMilliseconds, ex);

            if (!_disposed)
            {
                _state.OnNext(MutationState<TData>.CreateFailure(ex));
            }
        }

        if (_disposed)
        {
            return;
        }

        if (!cancelled)
        {
            if (error is null)
            {
                _options.OnSuccess.Invoke(args, data);
            }
            else
            {
                _options.OnFailure.Invoke(error);
            }
        }

        _options.OnSettled.Invoke();
    }
}

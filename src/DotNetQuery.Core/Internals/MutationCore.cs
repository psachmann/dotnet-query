namespace DotNetQuery.Core.Internals;

/// <summary>
/// The execution engine behind both mutation shapes. Running, retrying, cancelling, progress reporting
/// and the callback sequence live here once; <see cref="Mutation{TArgs,TData}"/> and
/// <see cref="Mutation{TArgs,TData,TProgress}"/> supply only the state record they publish, so neither
/// has to project the other's states (which would break reference-equality dedup in consumers).
/// </summary>
internal abstract class MutationCore<TArgs, TData, TProgress, TState> : IDisposable
    where TState : class, IMutationState<TData>
{
    private readonly EffectiveMutationOptions<TArgs, TData, TProgress> _options;
    private readonly QueryInstrumentation _instrumentation;
    private readonly BehaviorSubject<TState> _state;
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _execute = new();
    private readonly Subject<Unit> _cancel = new();
    private readonly CompositeDisposable _subscriptions = [];

    // Guards every state emission together with the fields below, so a progress report — which may
    // arrive on any thread — can neither interleave with nor follow the transition that ends its run.
    private readonly Lock _gate = new();
    private Run? _activeRun;
    private TProgress? _progress;
    private bool _disposed;

    protected MutationCore(
        EffectiveMutationOptions<TArgs, TData, TProgress> options,
        QueryInstrumentation instrumentation
    )
    {
        _options = options;
        _instrumentation = instrumentation;
        _state = new(CreateIdle());
        _isEnabled = new(options.IsEnabled);

        _subscriptions.Add(
            _execute
                .Select(args => Observable.FromAsync(ct => ExecuteAsync(args, ct)).TakeUntil(_cancel))
                .Switch()
                .Subscribe()
        );
    }

    public TState CurrentState => _state.Value;

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public IObservable<TState> State => _state.AsObservable();

    public IObservable<TData> Success =>
        _state.Where(state => state.Status == MutationStatus.Success).Select(state => state.CurrentData!);

    public IObservable<Exception> Failure =>
        _state.Where(state => state.Status == MutationStatus.Failure).Select(state => state.Error!);

    public IObservable<TState> Settled =>
        _state.Where(state => state.Status is MutationStatus.Success or MutationStatus.Failure);

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
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

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

    protected abstract TState CreateIdle();

    protected abstract TState CreateRunning(TProgress? progress);

    protected abstract TState CreateSuccess(TData data, TProgress? progress);

    protected abstract TState CreateFailure(Exception error, TProgress? progress);

    private async Task ExecuteAsync(TArgs args, CancellationToken cancellationToken)
    {
        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityMutationExecute);
        activity?.SetTag(QueryTelemetryTags.TagMutationName, _options.Name);
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        var run = new Run(cancellationToken);

        _options.OnMutate.Invoke(args);
        Start(run);
        _instrumentation.RecordMutationStart(_options.Name);

        TData data = default!;
        Exception? error = null;
        var cancelled = false;

        try
        {
            data = await _options
                .RetryHandler.ExecuteAsync(
                    ct =>
                    {
                        var attempt = Interlocked.Increment(ref attempts);

                        if (attempt > 1)
                        {
                            activity?.AddEvent(new ActivityEvent("retry"));
                        }

                        return _options.Mutator(args, BeginAttempt(run, attempt), ct);
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordMutationSuccess(_options.Name, stopwatch.Elapsed, attempts);

            Settle(run, progress => CreateSuccess(data, progress));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            cancelled = true;

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordMutationCancelled(_options.Name, stopwatch.Elapsed);

            Settle(run, _ => CreateIdle());
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            error = ex;

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, ex.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _instrumentation.RecordMutationFailure(_options.Name, stopwatch.Elapsed, ex, attempts);

            Settle(run, progress => CreateFailure(ex, progress));
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

    private void Start(Run run)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _activeRun = run;
            _progress = default;
            _state.OnNext(CreateRunning(default));
        }
    }

    private AttemptProgress BeginAttempt(Run run, int attempt)
    {
        lock (_gate)
        {
            run.Attempt = attempt;

            // A retried mutator starts over, so its progress does too. Skipped when nothing non-default
            // was reported, which keeps the state stream of a mutation that never reports exactly as it
            // was before progress existed.
            if (
                !_disposed
                && ReferenceEquals(_activeRun, run)
                && !EqualityComparer<TProgress?>.Default.Equals(_progress, default)
            )
            {
                _progress = default;
                _state.OnNext(CreateRunning(default));
            }
        }

        return new AttemptProgress(this, run, attempt);
    }

    private void Report(Run run, int attempt, TProgress value)
    {
        lock (_gate)
        {
            // Drops reports from an attempt that has ended: cancelled, superseded by a retry or a newer
            // execution, or already settled (the mutator may still hold its IProgress after returning).
            if (
                _disposed
                || !ReferenceEquals(_activeRun, run)
                || run.Attempt != attempt
                || run.CancellationToken.IsCancellationRequested
            )
            {
                return;
            }

            _progress = value;
            _state.OnNext(CreateRunning(value));
        }
    }

    private void Settle(Run run, Func<TProgress?, TState> createState)
    {
        lock (_gate)
        {
            // A run superseded by a newer Execute must not publish: the newer run already drives the
            // state, and an Idle or result from the old one would stomp its Running state mid-flight.
            if (_disposed || !ReferenceEquals(_activeRun, run))
            {
                return;
            }

            _activeRun = null;
            _state.OnNext(createState(_progress));
        }
    }

    /// <summary>One execution. Identity marks which run currently owns the state.</summary>
    private sealed class Run(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;

        /// <summary>The attempt currently allowed to report. Read and written under <c>_gate</c> only.</summary>
        public int Attempt { get; set; }
    }

    /// <summary>
    /// The <see cref="IProgress{T}"/> handed to one attempt. Reports synchronously on the caller's thread,
    /// unlike <see cref="Progress{T}"/>, which posts to the captured <see cref="SynchronizationContext"/>
    /// and could therefore deliver a report after the run has already settled.
    /// </summary>
    private sealed class AttemptProgress(MutationCore<TArgs, TData, TProgress, TState> owner, Run run, int attempt)
        : IProgress<TProgress>
    {
        public void Report(TProgress value) => owner.Report(run, attempt, value);
    }
}

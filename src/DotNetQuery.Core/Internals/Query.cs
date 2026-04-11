namespace DotNetQuery.Core.Internals;

internal sealed class Query<TArgs, TData> : IQuery
{
    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<QueryState<TData>> _state = new(QueryState<TData>.CreateIdle());
    private readonly Subject<Unit> _invalidate = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Lock _syncRoot = new();
    private DateTimeOffset? _lastSuccessAt;
    private int _subscriberCount;
    private bool _isStale;
    private bool _disposed;

    public Query(
        QueryKey key,
        TArgs args,
        EffectiveQueryOptions<TArgs, TData> options,
        IScheduler scheduler,
        QueryInstrumentation instrumentation
    )
    {
        _key = key;
        _args = args;
        _options = options;
        _scheduler = scheduler;
        _instrumentation = instrumentation;

        _subscriptions.Add(_invalidate.Select(_ => Observable.FromAsync(FetchAsync)).Switch().Subscribe());

        if (options.RefetchInterval is { } interval)
        {
            _subscriptions.Add(
                Observable.Interval(interval, _scheduler).Subscribe(_ => _invalidate.OnNext(Unit.Default))
            );
        }
    }

    public QueryKey Key => _key;

    public TimeSpan CacheTime => _options.CacheTime;

    public QueryState<TData> CurrentState => _state.Value;

    public IObservable<QueryState<TData>> State =>
        Observable.Create<QueryState<TData>>(observer =>
        {
            var subscription = _state.Subscribe(observer);

            lock (_syncRoot)
            {
                _subscriberCount++;
                if (_subscriberCount == 1 && _isStale)
                {
                    _isStale = false;
                    _invalidate.OnNext(Unit.Default);
                }
            }

            return () =>
            {
                subscription.Dispose();
                lock (_syncRoot)
                {
                    _subscriberCount--;
                }
            };
        });

    public void Refetch() => _invalidate.OnNext(Unit.Default);

    public void Invalidate()
    {
        if (_lastSuccessAt is { } last && _scheduler.Now - last < _options.StaleTime)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_subscriberCount > 0)
            {
                _invalidate.OnNext(Unit.Default);
            }
            else
            {
                _isStale = true;
            }
        }
    }

    public void Cancel() => _cancellationTokenSource.Cancel();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        _cancellationTokenSource.Cancel();
        _invalidate.OnCompleted();
        _invalidate.Dispose();
        _cancellationTokenSource.Dispose();
        _state.OnCompleted();
        _state.Dispose();
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _cancellationTokenSource.Token
        );
        var linkedToken = cts.Token;

        if (_disposed)
        {
            return;
        }

        var lastData = _state.Value.CurrentData;

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());

        var stopwatch = Stopwatch.StartNew();

        _state.OnNext(QueryState<TData>.CreateFetching(lastData));
        _instrumentation.RecordFetchStart(_key);

        try
        {
            var data = await _options.RetryHandler.ExecuteAsync(ct => _options.Fetcher(_args, ct), linkedToken);
            _lastSuccessAt = _scheduler.Now;
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, stopwatch.Elapsed.TotalMilliseconds);

            if (!_disposed)
            {
                _state.OnNext(QueryState<TData>.CreateSuccess(data, lastData));
            }
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            _instrumentation.RecordFetchCancelled(_key);

            if (!_disposed)
            {
                _state.OnNext(QueryState<TData>.CreateIdle(lastData));
            }
        }
        catch (Exception error)
        {
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, stopwatch.Elapsed.TotalMilliseconds, error);

            if (!_disposed)
            {
                _state.OnNext(QueryState<TData>.CreateFailure(error, lastData));
            }
        }
    }
}

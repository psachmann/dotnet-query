namespace DotNetQuery.Core.Internals;

internal sealed class Query<TArgs, TData> : IQuery
{
    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly IScheduler _scheduler;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<QueryState<TData>> _state = new(QueryState<TData>.CreateIdle());
    private readonly Subject<Unit> _invalidate = new();
    private readonly IDisposable _pipelineSubscription;
    private readonly IDisposable? _refetchSubscription;
    private readonly Lock _syncRoot = new();
    private DateTimeOffset? _lastSuccessAt;
    private int _subscriberCount;
    private bool _isStale;
    private bool _disposed;

    public Query(QueryKey key, TArgs args, EffectiveQueryOptions<TArgs, TData> options, IScheduler? scheduler = null)
    {
        _key = key;
        _args = args;
        _options = options;
        _scheduler = scheduler ?? Scheduler.Default;

        _pipelineSubscription = _invalidate.Select(_ => Observable.FromAsync(FetchAsync)).Switch().Subscribe();

        if (options.RefetchInterval is { } interval)
        {
            _refetchSubscription = Observable
                .Interval(interval, _scheduler)
                .Subscribe(_ => _invalidate.OnNext(Unit.Default));
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
        _refetchSubscription?.Dispose();
        _pipelineSubscription.Dispose();
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

        var lastData = _state.Value.CurrentData;

        _state.OnNext(QueryState<TData>.CreateFetching(lastData));

        try
        {
            var data = await _options.RetryHandler.ExecuteAsync(ct => _options.Fetcher(_args, ct), linkedToken);
            _lastSuccessAt = _scheduler.Now;

            _state.OnNext(QueryState<TData>.CreateSuccess(data, lastData));
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            _state.OnNext(QueryState<TData>.CreateIdle(lastData));
        }
        catch (Exception error)
        {
            _state.OnNext(QueryState<TData>.CreateFailure(error, lastData));
        }
    }
}

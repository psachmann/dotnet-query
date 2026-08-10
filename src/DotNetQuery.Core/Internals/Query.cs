namespace DotNetQuery.Core.Internals;

internal sealed class Query<TArgs, TData> : IQuery, IQueryInspector
    where TData : class
{
    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly string _metricName;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<QueryState<TData>> _state;
    private readonly Subject<FetchTrigger> _invalidate = new();
    private readonly Subject<Unit> _unsubscribed = new();
    private readonly Subject<Unit> _subscribed = new();
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
        _metricName = options.Name ?? (key.Parts.Count > 0 ? key.Parts[0].ToString() ?? "unknown" : "unknown");

        _state = options.InitialData is { } initial
            ? new BehaviorSubject<QueryState<TData>>(QueryState<TData>.CreateSuccess(initial))
            : new BehaviorSubject<QueryState<TData>>(QueryState<TData>.CreateIdle());

        _subscriptions.Add(
            _invalidate.Select(trigger => Observable.FromAsync(ct => FetchAsync(trigger, ct))).Switch().Subscribe()
        );

        if (options.RefetchInterval is { } interval)
        {
            _subscriptions.Add(
                Observable.Interval(interval, _scheduler).Subscribe(_ => Invalidate(FetchTrigger.Interval))
            );
        }
    }

    public QueryKey Key => _key;

    public TimeSpan CacheTime => _options.CacheTime;

    /// <summary>The low-cardinality name used to tag metrics for this query. See <see cref="QueryOptions{TArgs,TData}.Name"/>.</summary>
    public string MetricName => _metricName;

    public QueryState<TData> CurrentState => _state.Value;

    public QueryStatus Status => _state.Value.Status;

    public object? CurrentData => _state.Value.CurrentData;

    public DateTimeOffset? LastUpdatedAt
    {
        get
        {
            lock (_syncRoot)
            {
                return _lastSuccessAt;
            }
        }
    }

    public int ObserverCount => _subscriberCount;

    public IObservable<Unit> StateChanged => _state.Select(_ => Unit.Default);

    /// <summary>Fires when the last active <see cref="State"/> subscriber disposes.</summary>
    internal IObservable<Unit> Unsubscribed => _unsubscribed;

    /// <summary>Fires when the first <see cref="State"/> subscriber attaches after having none.</summary>
    internal IObservable<Unit> Subscribed => _subscribed;

    public IObservable<QueryState<TData>> State =>
        Observable.Create<QueryState<TData>>(observer =>
        {
            var subscription = _state.Subscribe(observer);
            var becameActive = false;

            lock (_syncRoot)
            {
                _subscriberCount++;
                becameActive = _subscriberCount == 1;

                if (becameActive && _isStale)
                {
                    _isStale = false;
                    _invalidate.OnNext(FetchTrigger.Stale);
                }
            }

            if (becameActive)
            {
                _subscribed.OnNext(Unit.Default);
            }

            return () =>
            {
                subscription.Dispose();

                var becameIdle = false;

                lock (_syncRoot)
                {
                    _subscriberCount--;
                    becameIdle = _subscriberCount == 0 && !_disposed;
                }

                if (becameIdle)
                {
                    _unsubscribed.OnNext(Unit.Default);
                }
            };
        });

    public void Refetch()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _invalidate.OnNext(FetchTrigger.Manual);
            }
        }
    }

    public void SetData(TData data)
    {
        lock (_syncRoot)
        {
            _lastSuccessAt = _scheduler.Now;
            _isStale = false;
        }

        if (!_disposed)
        {
            _state.OnNext(QueryState<TData>.CreateSuccess(data, _state.Value.CurrentData));
        }
    }

    internal Task PrefetchAsync(CancellationToken ct = default)
    {
        if (IsFresh())
        {
            return Task.CompletedTask;
        }

        return FetchAsync(FetchTrigger.Prefetch, ct);
    }

    public void Invalidate() => Invalidate(FetchTrigger.Invalidate);

    private void Invalidate(FetchTrigger trigger)
    {
        if (IsFresh())
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (_subscriberCount > 0)
            {
                _invalidate.OnNext(trigger);
            }
            else
            {
                _isStale = true;
            }
        }
    }

    private bool IsFresh()
    {
        lock (_syncRoot)
        {
            return _lastSuccessAt is { } last && _scheduler.Now - last < _options.StaleTime;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource previous;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            previous = _cancellationTokenSource;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public void Dispose()
    {
        CancellationTokenSource cts;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            cts = _cancellationTokenSource;
        }

        _subscriptions.Dispose();
        cts.Cancel();
        _invalidate.OnCompleted();
        _invalidate.Dispose();
        _unsubscribed.OnCompleted();
        _unsubscribed.Dispose();
        _subscribed.OnCompleted();
        _subscribed.Dispose();
        cts.Dispose();
        _state.OnCompleted();
        _state.Dispose();
    }

    private async Task FetchAsync(FetchTrigger trigger, CancellationToken cancellationToken)
    {
        CancellationTokenSource currentSource;

        lock (_syncRoot)
        {
            currentSource = _cancellationTokenSource;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, currentSource.Token);
        var linkedToken = cts.Token;

        if (_disposed)
        {
            return;
        }

        var lastData = _state.Value.CurrentData;

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());
        activity?.SetTag(QueryTelemetryTags.TagTrigger, trigger.ToTagValue());

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        _state.OnNext(QueryState<TData>.CreateFetching(lastData));
        _instrumentation.RecordFetchStart(_key, _metricName);

        try
        {
            var data = await _options.RetryHandler.ExecuteAsync(
                ct =>
                {
                    if (Interlocked.Increment(ref attempts) > 1)
                    {
                        activity?.AddEvent(new ActivityEvent("retry"));
                    }

                    return _options.Fetcher(_args, ct);
                },
                linkedToken
            );

            lock (_syncRoot)
            {
                _lastSuccessAt = _scheduler.Now;
            }

            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(
                _key,
                _metricName,
                stopwatch.Elapsed.TotalMilliseconds,
                attempts,
                trigger
            );

            if (!_disposed)
            {
                var emitData = lastData is not null && _options.DataComparer.Equals(data, lastData) ? lastData : data;
                _state.OnNext(QueryState<TData>.CreateSuccess(emitData, lastData));
            }
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordFetchCancelled(_key, _metricName, stopwatch.Elapsed.TotalMilliseconds, trigger);

            // Only the explicit Cancel() path (currentSource) should surface as Idle. When the outer
            // cancellationToken fired instead, this fetch was superseded by Switch() for a newer one,
            // which is already driving _state — emitting here would stomp its Fetching/Success state.
            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                _state.OnNext(QueryState<TData>.CreateIdle(lastData));
            }
        }
        catch (Exception error)
        {
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(
                _key,
                _metricName,
                stopwatch.Elapsed.TotalMilliseconds,
                error,
                attempts,
                trigger
            );

            if (!_disposed)
            {
                _state.OnNext(QueryState<TData>.CreateFailure(error, lastData));
            }
        }
    }
}

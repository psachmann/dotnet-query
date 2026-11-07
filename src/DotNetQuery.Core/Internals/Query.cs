namespace DotNetQuery.Core.Internals;

internal sealed class Query<TArgs, TData> : ICacheEntry
    where TData : class
{
    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly string _name;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<QueryState<TData>> _state;
    private readonly Subject<FetchTrigger> _invalidate = new();
    private readonly Subject<Unit> _unsubscribed = new();
    private readonly Subject<Unit> _subscribed = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Lock _syncRoot = new();
    private DateTimeOffset? _lastSuccessAt;
    private int _subscriberCount;
    private int _streamsInFlight;
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
        _name = options.Name ?? (key.Parts.Count > 0 ? key.Parts[0].ToString() ?? "unknown" : "unknown");

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
    public string Name => _name;

    public QueryState<TData> CurrentState => _state.Value;

    public QueryStatus Status => _state.Value.Status;

    public object? CurrentData => _state.Value.CurrentData;

    public object? LastData => _state.Value.LastData;

    public Exception? Error => _state.Value.Error;

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

    public bool IsStale
    {
        get
        {
            lock (_syncRoot)
            {
                return _isStale || _lastSuccessAt is not { } last || _scheduler.Now - last >= _options.StaleTime;
            }
        }
    }

    public IObservable<Unit> StateChanged => _state.Select(_ => Unit.Default);

    /// <summary>Fires when the last active <see cref="State"/> subscriber disposes.</summary>
    public IObservable<Unit> Unsubscribed => _unsubscribed;

    /// <summary>Fires when the first <see cref="State"/> subscriber attaches after having none.</summary>
    public IObservable<Unit> Subscribed => _subscribed;

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

            // A running stream is not restarted by an invalidation (focus, reconnect, interval tick):
            // re-opening it throws away everything received so far, and may be expensive. It is only
            // marked stale; Refetch() still restarts it.
            if (_subscriberCount > 0 && _streamsInFlight == 0)
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

        var stream = _options.Stream;
        var current = _state.Value;

        // A refetch that interrupts a stream must carry forward the last *completed* data, not the partial
        // value the interrupted stream had folded so far.
        var lastData =
            stream is not null && (current.IsStreaming || current.IsFetching) ? current.LastData : current.CurrentData;

        // The first fetch of a stream, and every refetch in Reset mode, shows chunks as they arrive and drops
        // the previous data; Replace keeps showing the previous data until the new stream completes.
        var showChunks = stream is not null && (stream.RefetchMode == StreamRefetchMode.Reset || lastData is null);
        TData? shownChunk = null;
        var chunks = 0;

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());
        activity?.SetTag(QueryTelemetryTags.TagQueryName, _name);
        activity?.SetTag(QueryTelemetryTags.TagTrigger, trigger.ToTagValue());

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;

        _state.OnNext(QueryState<TData>.CreateFetching(showChunks ? null : lastData));
        _instrumentation.RecordFetchStart(_key, _name);

        if (stream is not null)
        {
            lock (_syncRoot)
            {
                _streamsInFlight++;
            }
        }

        // One attempt of a streamed fetch: folds the whole stream, publishing each folded value if it is shown.
        async Task<TData> StreamAttemptAsync(StreamedFetch<TArgs, TData> streamedFetch, CancellationToken ct)
        {
            chunks = 0;

            // A retry starts the stream over, so drop what the failed attempt showed.
            if (shownChunk is not null)
            {
                shownChunk = null;

                if (!_disposed)
                {
                    _state.OnNext(QueryState<TData>.CreateFetching());
                }
            }

            return await streamedFetch
                .FetchAsync(
                    _args,
                    folded =>
                    {
                        // Stops a superseded or cancelled stream from publishing even if its producer
                        // ignores the token.
                        linkedToken.ThrowIfCancellationRequested();
                        chunks++;

                        if (showChunks && !_disposed)
                        {
                            shownChunk = folded;
                            _state.OnNext(QueryState<TData>.CreateStreaming(folded));
                        }
                    },
                    ct
                )
                .ConfigureAwait(false);
        }

        try
        {
            var data = await _options
                .RetryHandler.ExecuteAsync(
                    ct =>
                    {
                        if (Interlocked.Increment(ref attempts) > 1)
                        {
                            activity?.AddEvent(new ActivityEvent("retry"));
                        }

                        return stream is null ? _options.Fetcher(_args, ct) : StreamAttemptAsync(stream, ct);
                    },
                    linkedToken
                )
                .ConfigureAwait(false);

            EndStream();

            lock (_syncRoot)
            {
                _lastSuccessAt = _scheduler.Now;
            }

            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, _name, stopwatch.Elapsed, attempts, trigger);

            if (!_disposed)
            {
                var emitData = lastData is not null && _options.DataComparer.Equals(data, lastData) ? lastData : data;
                _state.OnNext(QueryState<TData>.CreateSuccess(emitData, lastData));
            }
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            EndStream();
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordFetchCancelled(_key, _name, stopwatch.Elapsed, trigger);

            // Only the explicit Cancel() path (currentSource) should surface as Idle. When the outer
            // cancellationToken fired instead, this fetch was superseded by Switch() for a newer one,
            // which is already driving _state — emitting here would stomp its Fetching/Success state.
            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                _state.OnNext(QueryState<TData>.CreateIdle(shownChunk ?? lastData));
            }
        }
        catch (Exception error)
        {
            EndStream();
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, _name, stopwatch.Elapsed, error, attempts, trigger);

            if (!_disposed)
            {
                _state.OnNext(QueryState<TData>.CreateFailure(error, shownChunk ?? lastData));
            }
        }

        // Runs before the terminal state is published, so a subscriber that invalidates in response to it
        // is not treated as invalidating a stream still in flight. Also tags the span with the chunk count.
        void EndStream()
        {
            if (stream is null)
            {
                return;
            }

            activity?.SetTag(QueryTelemetryTags.TagChunks, chunks);

            lock (_syncRoot)
            {
                _streamsInFlight--;
            }
        }
    }
}

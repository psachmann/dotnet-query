namespace DotNetQuery.Core.Internals;

using System.Reactive.Threading.Tasks;

internal sealed class Query<TArgs, TData> : ICacheEntry
    where TData : class
{
    private static readonly IObservable<bool> AlwaysEnabled = Observable.Return(true);

    /// <summary>
    /// One request to fetch. A prefetch additionally carries the caller's token and the completion its
    /// returned task waits on, so it can run through the same pipeline as every other fetch.
    /// </summary>
    private readonly record struct FetchRequest(
        FetchTrigger Trigger,
        CancellationToken CancellationToken = default,
        TaskCompletionSource? Completion = null
    );

    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly string _name;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<QueryState<TData>> _state;
    private readonly Subject<FetchRequest> _invalidate = new();
    private readonly Subject<Unit> _unsubscribed = new();
    private readonly Subject<Unit> _subscribed = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Lock _syncRoot = new();
    private DateTimeOffset? _lastSuccessAt;
    private int _subscriberCount;

    // Subscribers whose observer is enabled. Only these make an invalidation fetch; a disabled observer
    // still counts in _subscriberCount, so it keeps the entry out of eviction without driving fetches.
    private int _enabledSubscriberCount;

    // Identity of the fetch that currently owns _state, or null when none is in flight. A fetch
    // publishes only while it is this one, so a superseded fetch whose fetcher ignores its token can
    // neither overwrite the newer fetch's state nor stamp freshness. Guarded by _syncRoot.
    private object? _activeFetch;

    // Data is known to be out of date and no fetch for it has started yet: either nobody enabled is
    // subscribed (the next one to join fetches), or the invalidation arrived while a fetch was in
    // flight (see Invalidate).
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
            _invalidate.Select(request => Observable.FromAsync(ct => FetchAsync(request, ct))).Switch().Subscribe()
        );

        if (options.RefetchInterval is { } interval)
        {
            _subscriptions.Add(
                Observable
                    .Interval(interval, _scheduler)
                    .Subscribe(_ => Invalidate(FetchTrigger.Interval, staleIfInFlight: options.Stream is not null))
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

    internal bool IsDisposed => _disposed;

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

    public IObservable<QueryState<TData>> State => Observe(AlwaysEnabled);

    /// <summary>
    /// The state stream for one observer. The subscription always retains the entry, but it only drives
    /// fetching — on joining a stale entry and on invalidation — while <paramref name="isEnabled"/> is
    /// <c>true</c>.
    /// </summary>
    internal IObservable<QueryState<TData>> Observe(IObservable<bool> isEnabled) =>
        Observable.Create<QueryState<TData>>(observer =>
        {
            var subscription = _state.Subscribe(observer);
            var becameActive = false;

            lock (_syncRoot)
            {
                _subscriberCount++;
                becameActive = _subscriberCount == 1;
            }

            if (becameActive)
            {
                _subscribed.OnNext(Unit.Default);
            }

            var countedAsEnabled = false;
            var enabledSubscription = isEnabled
                .DistinctUntilChanged()
                .Subscribe(enabled =>
                {
                    lock (_syncRoot)
                    {
                        if (enabled == countedAsEnabled || _disposed)
                        {
                            return;
                        }

                        countedAsEnabled = enabled;

                        if (!enabled)
                        {
                            _enabledSubscriberCount--;
                            return;
                        }

                        _enabledSubscriberCount++;

                        if (_enabledSubscriberCount == 1 && _isStale && _activeFetch is null)
                        {
                            _isStale = false;
                            _invalidate.OnNext(new FetchRequest(FetchTrigger.Stale));
                        }
                    }
                });

            return () =>
            {
                enabledSubscription.Dispose();
                subscription.Dispose();

                var becameIdle = false;

                lock (_syncRoot)
                {
                    if (countedAsEnabled)
                    {
                        countedAsEnabled = false;
                        _enabledSubscriberCount--;
                    }

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
                _invalidate.OnNext(new FetchRequest(FetchTrigger.Manual));
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

        TaskCompletionSource completion;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            // A fetch is already on its way: wait for it instead of starting a second one.
            if (_activeFetch is not null)
            {
                return WhenSettledAsync(ct);
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _invalidate.OnNext(new FetchRequest(FetchTrigger.Prefetch, ct, completion));
        }

        return completion.Task;
    }

    private async Task WhenSettledAsync(CancellationToken ct)
    {
        try
        {
            await _state
                .FirstOrDefaultAsync(state => state is { IsFetching: false, IsStreaming: false })
                .ToTask(ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The caller stopped waiting. The fetch belongs to whoever started it and carries on.
        }
        catch (ObjectDisposedException)
        {
            // The entry was evicted while waiting.
        }
    }

    public void Invalidate() => Invalidate(FetchTrigger.Invalidate, staleIfInFlight: true);

    /// <summary>
    /// The invalidation an observer issues when it attaches to this entry. Unlike <see cref="Invalidate()"/>
    /// it simply joins a fetch that is already in flight.
    /// </summary>
    internal void InvalidateOnAttach() => Invalidate(FetchTrigger.Invalidate, staleIfInFlight: false);

    private void Invalidate(FetchTrigger trigger, bool staleIfInFlight)
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

            // A fetch in flight is never restarted by an invalidation (focus, reconnect, interval tick,
            // a mutation's InvalidateKeys): restarting on each one means a fetch slower than the
            // invalidation rate never completes, and for a stream it throws away everything received so
            // far. The entry is marked stale instead. A regular fetch is then followed by one more once
            // it settles, because it may predate whatever caused the invalidation; a stream is not
            // re-opened, as that may be expensive. Refetch() still restarts either.
            if (_activeFetch is not null)
            {
                _isStale |= staleIfInFlight;
            }
            else if (_enabledSubscriberCount > 0)
            {
                _invalidate.OnNext(new FetchRequest(trigger));
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

    private async Task FetchAsync(FetchRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await FetchAsync(request.Trigger, request.CancellationToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            request.Completion?.TrySetResult();
        }
    }

    /// <param name="trigger">What caused the fetch; tagged on the span and metrics.</param>
    /// <param name="prefetchToken">The caller's token when the fetch is a prefetch.</param>
    /// <param name="cancellationToken">Cancelled by <c>Switch()</c> when a newer fetch supersedes this one.</param>
    private async Task FetchAsync(
        FetchTrigger trigger,
        CancellationToken prefetchToken,
        CancellationToken cancellationToken
    )
    {
        if (_disposed)
        {
            return;
        }

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());
        activity?.SetTag(QueryTelemetryTags.TagQueryName, _name);
        activity?.SetTag(QueryTelemetryTags.TagTrigger, trigger.ToTagValue());

        var fetch = new object();
        var stream = _options.Stream;
        CancellationTokenSource currentSource;
        CancellationTokenSource linkedSource;
        TData? lastData;
        bool showChunks;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            currentSource = _cancellationTokenSource;
            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                currentSource.Token,
                prefetchToken
            );

            var current = _state.Value;

            // A refetch that interrupts a stream must carry forward the last *completed* data, not the
            // partial value the interrupted stream had folded so far.
            lastData =
                stream is not null && (current.IsStreaming || current.IsFetching)
                    ? current.LastData
                    : current.CurrentData;

            // The first fetch of a stream, and every refetch in Reset mode, shows chunks as they arrive and
            // drops the previous data; Replace keeps showing the previous data until the new stream completes.
            showChunks = stream is not null && (stream.RefetchMode == StreamRefetchMode.Reset || lastData is null);

            _activeFetch = fetch;
            _isStale = false;
            _state.OnNext(QueryState<TData>.CreateFetching(showChunks ? null : lastData));
        }

        using var cts = linkedSource;
        var linkedToken = cts.Token;
        TData? shownChunk = null;
        var chunks = 0;
        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        var refetch = false;

        _instrumentation.RecordFetchStart(_key, _name);

        // Publishes a state of the fetch in progress, unless a newer fetch has taken over.
        bool Publish(QueryState<TData> state)
        {
            lock (_syncRoot)
            {
                if (_disposed || !ReferenceEquals(_activeFetch, fetch))
                {
                    return false;
                }

                _state.OnNext(state);
                return true;
            }
        }

        // Ends this fetch and publishes its terminal state — atomically, so an invalidation can never
        // slip a new fetch in between the two — unless a newer fetch has taken over, in which case
        // nothing is published. The fetch is ended before the state goes out, so a subscriber that
        // invalidates in response to it is not treated as invalidating a fetch still in flight.
        bool Settle(QueryState<TData> state, bool succeeded = false, bool refetchIfStale = true)
        {
            lock (_syncRoot)
            {
                if (!ReferenceEquals(_activeFetch, fetch))
                {
                    return false;
                }

                _activeFetch = null;

                if (_disposed)
                {
                    return false;
                }

                if (succeeded)
                {
                    _lastSuccessAt = _scheduler.Now;
                }

                refetch = refetchIfStale && _isStale && _enabledSubscriberCount > 0;
                _state.OnNext(state);
                return true;
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
                Publish(QueryState<TData>.CreateFetching());
            }

            return await streamedFetch
                .FetchAsync(
                    _args,
                    folded =>
                    {
                        // Stops a superseded or cancelled stream from folding on even if its producer
                        // ignores the token.
                        linkedToken.ThrowIfCancellationRequested();
                        chunks++;

                        if (showChunks && Publish(QueryState<TData>.CreateStreaming(folded)))
                        {
                            shownChunk = folded;
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

            // A fetcher that ignores its token returns normally after being cancelled or superseded.
            // Its result is discarded like any other cancelled fetch's.
            linkedToken.ThrowIfCancellationRequested();

            var emitData = lastData is not null && _options.DataComparer.Equals(data, lastData) ? lastData : data;

            // A stream's staleness is left for the next subscriber: it is not re-opened on its own.
            if (
                !Settle(
                    QueryState<TData>.CreateSuccess(emitData, lastData),
                    succeeded: true,
                    refetchIfStale: stream is null
                )
            )
            {
                throw new OperationCanceledException(linkedToken);
            }

            stopwatch.Stop();
            SetStreamTags();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, _name, stopwatch.Elapsed, attempts, trigger);
        }
        catch (Exception) when (linkedToken.IsCancellationRequested)
        {
            // Only an explicit cancel should surface as Idle. When the outer cancellationToken fired
            // instead, this fetch was superseded by Switch() for a newer one, which is already driving
            // _state — Settle publishes nothing then.
            var prefetchCancelled = prefetchToken.IsCancellationRequested && !currentSource.IsCancellationRequested;

            lock (_syncRoot)
            {
                // A cancelled prefetch leaves subscribers that joined it without data; fetch for them.
                _isStale |= prefetchCancelled && ReferenceEquals(_activeFetch, fetch);
            }

            Settle(QueryState<TData>.CreateIdle(shownChunk ?? lastData), refetchIfStale: prefetchCancelled);

            stopwatch.Stop();
            SetStreamTags();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordFetchCancelled(_key, _name, stopwatch.Elapsed, trigger);
        }
        catch (Exception error)
        {
            Settle(QueryState<TData>.CreateFailure(error, shownChunk ?? lastData), refetchIfStale: stream is null);

            stopwatch.Stop();
            SetStreamTags();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, _name, stopwatch.Elapsed, error, attempts, trigger);
        }

        if (refetch)
        {
            lock (_syncRoot)
            {
                if (!_disposed && _isStale && _activeFetch is null)
                {
                    _invalidate.OnNext(new FetchRequest(FetchTrigger.Invalidate));
                }
            }
        }

        void SetStreamTags()
        {
            if (stream is not null)
            {
                activity?.SetTag(QueryTelemetryTags.TagChunks, chunks);
            }
        }
    }
}

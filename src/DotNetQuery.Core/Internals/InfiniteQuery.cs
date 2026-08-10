namespace DotNetQuery.Core.Internals;

internal sealed class InfiniteQuery<TArgs, TData, TPageParam> : IQuery, ICacheEntry
    where TData : class
{
    private enum FetchDirection
    {
        RefetchAll,
        FetchNext,
        FetchPrevious,
    }

    private readonly record struct FetchCommand(FetchDirection Direction, FetchTrigger Trigger);

    /// <summary>
    /// Accumulates fetcher invocations across every page fetched by one <see cref="ExecuteAsync"/> run.
    /// A refetch-all issues one fetch per loaded page, so raw attempt counts cannot be summed — only
    /// attempts beyond each page's first are <see cref="Retries"/>; <see cref="Attempts"/> counts every
    /// invocation so exit paths can tell whether the fetcher ever ran.
    /// </summary>
    private sealed class AttemptCounter
    {
        public int Attempts;
        public int Retries;
    }

    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly string _metricName;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<InfiniteQueryState<TData, TPageParam>> _state;
    private readonly Subject<FetchCommand> _command = new();
    private readonly Subject<Unit> _unsubscribed = new();
    private readonly Subject<Unit> _subscribed = new();
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Lock _syncRoot = new();
    private readonly List<TData> _pages = [];
    private readonly List<TPageParam> _pageParams = [];
    private DateTimeOffset? _lastSuccessAt;
    private int _subscriberCount;
    private bool _isStale;
    private bool _disposed;

    public InfiniteQuery(
        QueryKey key,
        TArgs args,
        EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> options,
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

        if (options.InitialData is { } initialData)
        {
            _pages.Add(initialData);
            _pageParams.Add(options.InitialPageParam);
            var (hasNext, hasPrev) = ComputeHasMorePages(_pages, _pageParams);
            _state = new BehaviorSubject<InfiniteQueryState<TData, TPageParam>>(
                InfiniteQueryState<TData, TPageParam>.CreateSuccess([.. _pages], [.. _pageParams], hasNext, hasPrev)
            );
        }
        else
        {
            _state = new BehaviorSubject<InfiniteQueryState<TData, TPageParam>>(
                InfiniteQueryState<TData, TPageParam>.CreateIdle()
            );
        }

        _subscriptions.Add(
            _command.Select(cmd => Observable.FromAsync(ct => ExecuteAsync(cmd, ct))).Switch().Subscribe()
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

    /// <summary>The low-cardinality name used to tag metrics for this query. See <see cref="InfiniteQueryOptions{TArgs,TData,TPageParam}.Name"/>.</summary>
    public string MetricName => _metricName;

    public InfiniteQueryState<TData, TPageParam> CurrentState => _state.Value;

    public QueryStatus Status => _state.Value.Status;

    public object? CurrentData
    {
        get
        {
            lock (_syncRoot)
            {
                // Snapshot — a live view over _pages would race with fetches mutating the list.
                return _pages.Count > 0 ? _pages.ToArray() : null;
            }
        }
    }

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
    public IObservable<Unit> Unsubscribed => _unsubscribed;

    /// <summary>Fires when the first <see cref="State"/> subscriber attaches after having none.</summary>
    public IObservable<Unit> Subscribed => _subscribed;

    public IObservable<InfiniteQueryState<TData, TPageParam>> State =>
        Observable.Create<InfiniteQueryState<TData, TPageParam>>(observer =>
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
                    _command.OnNext(new FetchCommand(FetchDirection.RefetchAll, FetchTrigger.Stale));
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

    public void FetchNextPage() => Send(FetchDirection.FetchNext, FetchTrigger.Manual);

    public void FetchPreviousPage() => Send(FetchDirection.FetchPrevious, FetchTrigger.Manual);

    public void Refetch() => Send(FetchDirection.RefetchAll, FetchTrigger.Manual);

    private void Send(FetchDirection direction, FetchTrigger trigger)
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _command.OnNext(new FetchCommand(direction, trigger));
            }
        }
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
                _command.OnNext(new FetchCommand(FetchDirection.RefetchAll, trigger));
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
        _command.OnCompleted();
        _command.Dispose();
        _unsubscribed.OnCompleted();
        _unsubscribed.Dispose();
        _subscribed.OnCompleted();
        _subscribed.Dispose();
        cts.Dispose();
        _state.OnCompleted();
        _state.Dispose();
    }

    private async Task ExecuteAsync(FetchCommand command, CancellationToken cancellationToken)
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

        List<TData> snapshotPages;
        List<TPageParam> snapshotParams;
        bool snapshotHasNext;
        bool snapshotHasPrev;

        lock (_syncRoot)
        {
            snapshotPages = [.. _pages];
            snapshotParams = [.. _pageParams];
            snapshotHasNext = _state.Value.HasNextPage;
            snapshotHasPrev = _state.Value.HasPreviousPage;
        }

        // Resolve the page params to fetch up front — this is both the no-op check (before any
        // telemetry starts) and the only invocation of the user's page-param delegates this run.
        List<TPageParam>? refetchParams = null;
        TPageParam boundaryParam = default!;

        switch (command.Direction)
        {
            case FetchDirection.RefetchAll:
                refetchParams = snapshotParams.Count > 0 ? snapshotParams : [_options.InitialPageParam];
                break;

            case FetchDirection.FetchNext:
                if (snapshotPages.Count == 0)
                {
                    return;
                }

                var nextParam = _options.GetNextPageParam(
                    new InfinitePageInfo<TData, TPageParam>(
                        snapshotPages[^1],
                        snapshotPages,
                        snapshotParams[^1],
                        snapshotParams
                    )
                );

                if (!nextParam.HasValue)
                {
                    return;
                }

                boundaryParam = nextParam.Value;
                break;

            case FetchDirection.FetchPrevious:
                if (snapshotPages.Count == 0 || _options.GetPreviousPageParam is null)
                {
                    return;
                }

                var prevParam = _options.GetPreviousPageParam(
                    new InfinitePageInfo<TData, TPageParam>(
                        snapshotPages[0],
                        snapshotPages,
                        snapshotParams[0],
                        snapshotParams
                    )
                );

                if (!prevParam.HasValue)
                {
                    return;
                }

                boundaryParam = prevParam.Value;
                break;

            default:
                return;
        }

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());
        activity?.SetTag(QueryTelemetryTags.TagQueryName, _metricName);
        activity?.SetTag(QueryTelemetryTags.TagTrigger, command.Trigger.ToTagValue());
        activity?.SetTag(QueryTelemetryTags.TagDirection, ToTagValue(command.Direction));

        var stopwatch = Stopwatch.StartNew();
        var counter = new AttemptCounter();

        _instrumentation.RecordFetchStart(_key, _metricName);

        try
        {
            var didFetch = command.Direction switch
            {
                FetchDirection.RefetchAll => await ExecuteRefetchAllAsync(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev,
                    refetchParams!,
                    activity,
                    counter,
                    linkedToken
                ),
                FetchDirection.FetchNext => await ExecuteFetchNextAsync(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev,
                    boundaryParam,
                    activity,
                    counter,
                    linkedToken
                ),
                FetchDirection.FetchPrevious => await ExecuteFetchPreviousAsync(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev,
                    boundaryParam,
                    activity,
                    counter,
                    linkedToken
                ),
                _ => false,
            };

            stopwatch.Stop();

            if (!didFetch)
            {
                // The entry was disposed mid-run — balance RecordFetchStart without stamping
                // freshness or reporting a successful fetch that never took effect.
                activity?.SetTag(QueryTelemetryTags.TagAttempts, counter.Attempts == 0 ? 0 : counter.Retries + 1);
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                _instrumentation.RecordFetchCancelled(_key, _metricName, stopwatch.Elapsed, command.Trigger);
                return;
            }

            lock (_syncRoot)
            {
                _lastSuccessAt = _scheduler.Now;
            }

            var attempts = counter.Retries + 1;
            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagPages, refetchParams?.Count ?? 1);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, _metricName, stopwatch.Elapsed, attempts, command.Trigger);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, counter.Attempts == 0 ? 0 : counter.Retries + 1);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordFetchCancelled(_key, _metricName, stopwatch.Elapsed, command.Trigger);

            // Only the explicit Cancel() path (currentSource) should roll state back. When the outer
            // cancellationToken fired instead, this run was superseded by Switch() for a newer command,
            // which is already driving _state — emitting here would stomp its Fetching/Success state.
            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                _state.OnNext(
                    snapshotPages.Count > 0
                        ? InfiniteQueryState<TData, TPageParam>.CreateSuccess(
                            snapshotPages,
                            snapshotParams,
                            snapshotHasNext,
                            snapshotHasPrev
                        )
                        : InfiniteQueryState<TData, TPageParam>.CreateIdle()
                );
            }
        }
        catch (Exception error)
        {
            stopwatch.Stop();

            var attempts = counter.Retries + 1;
            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, _metricName, stopwatch.Elapsed, error, attempts, command.Trigger);

            if (!_disposed)
            {
                _state.OnNext(
                    InfiniteQueryState<TData, TPageParam>.CreateFailure(
                        error,
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev
                    )
                );
            }
        }
    }

    private static string ToTagValue(FetchDirection direction) =>
        direction switch
        {
            FetchDirection.RefetchAll => "refetch_all",
            FetchDirection.FetchNext => "next",
            FetchDirection.FetchPrevious => "previous",
            _ => "unknown",
        };

    /// <summary>
    /// Runs one page fetch through the retry handler, recording a retry event on <paramref name="activity"/>
    /// and bumping <paramref name="counter"/> for every attempt beyond the first.
    /// </summary>
    private Task<TData> FetchPageAsync(
        TPageParam pageParam,
        Activity? activity,
        AttemptCounter counter,
        CancellationToken ct
    )
    {
        var pageAttempts = 0;

        return _options.RetryHandler.ExecuteAsync(
            token =>
            {
                Interlocked.Increment(ref counter.Attempts);

                if (Interlocked.Increment(ref pageAttempts) > 1)
                {
                    Interlocked.Increment(ref counter.Retries);
                    activity?.AddEvent(new ActivityEvent("retry"));
                }

                return _options.Fetcher(_args, pageParam, token);
            },
            ct
        );
    }

    private async Task<bool> ExecuteRefetchAllAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        List<TPageParam> paramsToFetch,
        Activity? activity,
        AttemptCounter counter,
        CancellationToken ct
    )
    {
        if (_disposed)
        {
            return false;
        }

        _state.OnNext(
            InfiniteQueryState<TData, TPageParam>.CreateFetching(
                snapshotPages,
                snapshotParams,
                snapshotHasNext,
                snapshotHasPrev
            )
        );

        var newPages = new List<TData>(paramsToFetch.Count);
        var newParams = new List<TPageParam>(paramsToFetch.Count);

        foreach (var param in paramsToFetch)
        {
            ct.ThrowIfCancellationRequested();
            var page = await FetchPageAsync(param, activity, counter, ct);

            // Keep the previous instance when the re-fetched page is structurally identical, so
            // consumers relying on reference equality don't observe a change that isn't one.
            if (
                newPages.Count < snapshotPages.Count
                && _options.DataComparer.Equals(page, snapshotPages[newPages.Count])
            )
            {
                page = snapshotPages[newPages.Count];
            }

            newPages.Add(page);
            newParams.Add(param);
        }

        if (_disposed)
        {
            return false;
        }

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);

        lock (_syncRoot)
        {
            _pages.Clear();
            _pages.AddRange(newPages);
            _pageParams.Clear();
            _pageParams.AddRange(newParams);
        }

        _state.OnNext(InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev));
        return true;
    }

    private async Task<bool> ExecuteFetchNextAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        TPageParam nextParam,
        Activity? activity,
        AttemptCounter counter,
        CancellationToken ct
    )
    {
        if (_disposed)
        {
            return false;
        }

        _state.OnNext(
            InfiniteQueryState<TData, TPageParam>.CreateFetchingNext(
                snapshotPages,
                snapshotParams,
                snapshotHasNext,
                snapshotHasPrev
            )
        );

        var page = await FetchPageAsync(nextParam, activity, counter, ct);

        if (_disposed)
        {
            return false;
        }

        List<TData> newPages;
        List<TPageParam> newParams;

        lock (_syncRoot)
        {
            _pages.Add(page);
            _pageParams.Add(nextParam);

            if (_options.MaxPages is { } maxPages && _pages.Count > maxPages)
            {
                var removeCount = _pages.Count - maxPages;
                _pages.RemoveRange(0, removeCount);
                _pageParams.RemoveRange(0, removeCount);
            }

            newPages = [.. _pages];
            newParams = [.. _pageParams];
        }

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);
        _state.OnNext(InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev));
        return true;
    }

    private async Task<bool> ExecuteFetchPreviousAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        TPageParam prevParam,
        Activity? activity,
        AttemptCounter counter,
        CancellationToken ct
    )
    {
        if (_disposed)
        {
            return false;
        }

        _state.OnNext(
            InfiniteQueryState<TData, TPageParam>.CreateFetchingPrevious(
                snapshotPages,
                snapshotParams,
                snapshotHasNext,
                snapshotHasPrev
            )
        );

        var page = await FetchPageAsync(prevParam, activity, counter, ct);

        if (_disposed)
        {
            return false;
        }

        List<TData> newPages;
        List<TPageParam> newParams;

        lock (_syncRoot)
        {
            _pages.Insert(0, page);
            _pageParams.Insert(0, prevParam);

            if (_options.MaxPages is { } maxPages && _pages.Count > maxPages)
            {
                var removeCount = _pages.Count - maxPages;
                _pages.RemoveRange(maxPages, removeCount);
                _pageParams.RemoveRange(maxPages, removeCount);
            }

            newPages = [.. _pages];
            newParams = [.. _pageParams];
        }

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);
        _state.OnNext(InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev));
        return true;
    }

    private (bool hasNext, bool hasPrev) ComputeHasMorePages(List<TData> pages, List<TPageParam> pageParams)
    {
        if (pages.Count == 0)
        {
            return (false, false);
        }

        var lastInfo = new InfinitePageInfo<TData, TPageParam>(pages[^1], pages, pageParams[^1], pageParams);
        var hasNext = _options.GetNextPageParam(lastInfo).HasValue;

        var hasPrev = false;
        if (_options.GetPreviousPageParam is { } getPrev)
        {
            var firstInfo = new InfinitePageInfo<TData, TPageParam>(pages[0], pages, pageParams[0], pageParams);
            hasPrev = getPrev(firstInfo).HasValue;
        }

        return (hasNext, hasPrev);
    }
}

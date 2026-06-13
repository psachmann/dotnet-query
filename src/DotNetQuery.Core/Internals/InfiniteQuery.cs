namespace DotNetQuery.Core.Internals;

internal sealed class InfiniteQuery<TArgs, TData, TPageParam> : IQuery, IQueryInspector
{
    private enum FetchDirection
    {
        RefetchAll,
        FetchNext,
        FetchPrevious,
    }

    private readonly QueryKey _key;
    private readonly TArgs _args;
    private readonly EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BehaviorSubject<InfiniteQueryState<TData, TPageParam>> _state;
    private readonly Subject<FetchDirection> _command = new();
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
                Observable.Interval(interval, _scheduler).Subscribe(_ => _command.OnNext(FetchDirection.RefetchAll))
            );
        }
    }

    public QueryKey Key => _key;

    public TimeSpan CacheTime => _options.CacheTime;

    public InfiniteQueryState<TData, TPageParam> CurrentState => _state.Value;

    public QueryStatus Status => _state.Value.Status;

    public object? CurrentData
    {
        get
        {
            lock (_syncRoot)
            {
                return _pages.Count > 0 ? (object)_pages.AsReadOnly() : null;
            }
        }
    }

    public DateTimeOffset? LastUpdatedAt => _lastSuccessAt;

    public int ObserverCount => _subscriberCount;

    public IObservable<Unit> StateChanged => _state.Select(_ => Unit.Default);

    public IObservable<InfiniteQueryState<TData, TPageParam>> State =>
        Observable.Create<InfiniteQueryState<TData, TPageParam>>(observer =>
        {
            var subscription = _state.Subscribe(observer);

            lock (_syncRoot)
            {
                _subscriberCount++;
                if (_subscriberCount == 1 && _isStale)
                {
                    _isStale = false;
                    _command.OnNext(FetchDirection.RefetchAll);
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

    public void FetchNextPage() => _command.OnNext(FetchDirection.FetchNext);

    public void FetchPreviousPage() => _command.OnNext(FetchDirection.FetchPrevious);

    public void Refetch() => _command.OnNext(FetchDirection.RefetchAll);

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
                _command.OnNext(FetchDirection.RefetchAll);
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
        _command.OnCompleted();
        _command.Dispose();
        _cancellationTokenSource.Dispose();
        _state.OnCompleted();
        _state.Dispose();
    }

    private async Task ExecuteAsync(FetchDirection direction, CancellationToken cancellationToken)
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

        // No-op checks before starting telemetry
        if (!ShouldFetch(direction, snapshotPages, snapshotParams))
        {
            return;
        }

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());

        var stopwatch = Stopwatch.StartNew();
        _instrumentation.RecordFetchStart(_key);

        try
        {
            switch (direction)
            {
                case FetchDirection.RefetchAll:
                    await ExecuteRefetchAllAsync(
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        linkedToken
                    );
                    break;
                case FetchDirection.FetchNext:
                    await ExecuteFetchNextAsync(
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        linkedToken
                    );
                    break;
                case FetchDirection.FetchPrevious:
                    await ExecuteFetchPreviousAsync(
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        linkedToken
                    );
                    break;
            }

            _lastSuccessAt = _scheduler.Now;
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _instrumentation.RecordFetchCancelled(_key);

            if (!_disposed)
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

            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, stopwatch.Elapsed.TotalMilliseconds, error);

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

    private bool ShouldFetch(FetchDirection direction, List<TData> snapshotPages, List<TPageParam> snapshotParams)
    {
        switch (direction)
        {
            case FetchDirection.RefetchAll:
                return true;

            case FetchDirection.FetchNext:
                if (snapshotPages.Count == 0)
                {
                    return false;
                }

                return _options
                    .GetNextPageParam(
                        new InfinitePageInfo<TData, TPageParam>(
                            snapshotPages[^1],
                            snapshotPages,
                            snapshotParams[^1],
                            snapshotParams
                        )
                    )
                    .HasValue;

            case FetchDirection.FetchPrevious:
                if (snapshotPages.Count == 0 || _options.GetPreviousPageParam is null)
                {
                    return false;
                }

                return _options
                    .GetPreviousPageParam(
                        new InfinitePageInfo<TData, TPageParam>(
                            snapshotPages[0],
                            snapshotPages,
                            snapshotParams[0],
                            snapshotParams
                        )
                    )
                    .HasValue;

            default:
                return false;
        }
    }

    private async Task ExecuteRefetchAllAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        CancellationToken ct
    )
    {
        if (_disposed)
        {
            return;
        }

        var paramsToFetch = snapshotParams.Count > 0 ? snapshotParams : [_options.InitialPageParam];

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
            var page = await _options.RetryHandler.ExecuteAsync(tok => _options.Fetcher(_args, param, tok), ct);
            newPages.Add(page);
            newParams.Add(param);
        }

        if (_disposed)
        {
            return;
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
    }

    private async Task ExecuteFetchNextAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        CancellationToken ct
    )
    {
        if (_disposed || snapshotPages.Count == 0)
        {
            return;
        }

        var nextParamResult = _options.GetNextPageParam(
            new InfinitePageInfo<TData, TPageParam>(
                snapshotPages[^1],
                snapshotPages,
                snapshotParams[^1],
                snapshotParams
            )
        );

        if (!nextParamResult.HasValue)
        {
            return;
        }

        var nextParam = nextParamResult.Value;

        _state.OnNext(
            InfiniteQueryState<TData, TPageParam>.CreateFetchingNext(
                snapshotPages,
                snapshotParams,
                snapshotHasNext,
                snapshotHasPrev
            )
        );

        var page = await _options.RetryHandler.ExecuteAsync(tok => _options.Fetcher(_args, nextParam, tok), ct);

        if (_disposed)
        {
            return;
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
    }

    private async Task ExecuteFetchPreviousAsync(
        List<TData> snapshotPages,
        List<TPageParam> snapshotParams,
        bool snapshotHasNext,
        bool snapshotHasPrev,
        CancellationToken ct
    )
    {
        if (_disposed || snapshotPages.Count == 0 || _options.GetPreviousPageParam is null)
        {
            return;
        }

        var prevParamResult = _options.GetPreviousPageParam(
            new InfinitePageInfo<TData, TPageParam>(snapshotPages[0], snapshotPages, snapshotParams[0], snapshotParams)
        );

        if (!prevParamResult.HasValue)
        {
            return;
        }

        var prevParam = prevParamResult.Value;

        _state.OnNext(
            InfiniteQueryState<TData, TPageParam>.CreateFetchingPrevious(
                snapshotPages,
                snapshotParams,
                snapshotHasNext,
                snapshotHasPrev
            )
        );

        var page = await _options.RetryHandler.ExecuteAsync(tok => _options.Fetcher(_args, prevParam, tok), ct);

        if (_disposed)
        {
            return;
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

namespace Fetchling.Core.Internals;

internal sealed class InfiniteQuery<TArgs, TData, TPageParam> : ICacheEntry
    where TData : class
{
    private enum FetchDirection
    {
        RefetchAll,
        FetchNext,
        FetchPrevious,
    }

    // PageParam is the page to fetch for a next/previous command, resolved when the command was issued.
    private readonly record struct FetchCommand(
        FetchDirection Direction,
        FetchTrigger Trigger,
        TPageParam PageParam = default!
    );

    private static readonly IObservable<bool> AlwaysEnabled = Observable.Return(true);

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
    private readonly string _name;
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

    // Subscribers whose observer is enabled. Only these make an invalidation fetch; a disabled observer
    // still counts in _subscriberCount, so it keeps the entry out of eviction without driving fetches.
    private int _enabledSubscriberCount;

    // Identity of the run that currently owns _state and _pages, or null when none is in flight. A run
    // commits and publishes only while it is this one, so a superseded run whose fetcher ignores its
    // token can neither overwrite the newer run's state nor append to pages it no longer matches.
    // Guarded by _syncRoot.
    private object? _activeFetch;

    // Data is known to be out of date and no refetch for it has started yet: either nobody enabled is
    // subscribed (the next one to join fetches), or the invalidation arrived while a run was in flight
    // (it is followed by a refetch once it settles).
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
        _name = options.Name ?? (key.Parts.Count > 0 ? key.Parts[0].ToString() ?? "unknown" : "unknown");

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
                Observable
                    .Interval(interval, _scheduler)
                    .Subscribe(_ => Invalidate(FetchTrigger.Interval, staleIfInFlight: false, respectStaleTime: true))
            );
        }
    }

    public QueryKey Key => _key;

    public TimeSpan CacheTime => _options.CacheTime;

    /// <summary>The low-cardinality name used to tag metrics for this query. See <see cref="InfiniteQueryOptions{TArgs,TData,TPageParam}.Name"/>.</summary>
    public string Name => _name;

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

    public object? LastData => null;

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

    public IObservable<InfiniteQueryState<TData, TPageParam>> State => Observe(AlwaysEnabled);

    /// <summary>
    /// The state stream for one observer. The subscription always retains the entry, but it only drives
    /// fetching — on joining a stale entry and on invalidation — while <paramref name="isEnabled"/> is
    /// <c>true</c>.
    /// </summary>
    internal IObservable<InfiniteQueryState<TData, TPageParam>> Observe(IObservable<bool> isEnabled) =>
        Observable.Create<InfiniteQueryState<TData, TPageParam>>(observer =>
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
                            _command.OnNext(new FetchCommand(FetchDirection.RefetchAll, FetchTrigger.Stale));
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

    // The page to fetch is resolved here, before the command enters Switch(): a call that turns out to
    // be a no-op (nothing loaded yet, or no further page) must not cancel the run in flight, and a
    // throwing page-param delegate surfaces to the caller instead of faulting the command pipeline.
    public void FetchNextPage()
    {
        lock (_syncRoot)
        {
            if (_disposed || _pages.Count == 0)
            {
                return;
            }

            var param = _options.GetNextPageParam(
                new InfinitePageInfo<TData, TPageParam>(_pages[^1], [.. _pages], _pageParams[^1], [.. _pageParams])
            );

            if (param.HasValue)
            {
                _command.OnNext(new FetchCommand(FetchDirection.FetchNext, FetchTrigger.Manual, param.Value));
            }
        }
    }

    public void FetchPreviousPage()
    {
        lock (_syncRoot)
        {
            if (_disposed || _pages.Count == 0 || _options.GetPreviousPageParam is not { } getPreviousPageParam)
            {
                return;
            }

            var param = getPreviousPageParam(
                new InfinitePageInfo<TData, TPageParam>(_pages[0], [.. _pages], _pageParams[0], [.. _pageParams])
            );

            if (param.HasValue)
            {
                _command.OnNext(new FetchCommand(FetchDirection.FetchPrevious, FetchTrigger.Manual, param.Value));
            }
        }
    }

    public void Refetch()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _command.OnNext(new FetchCommand(FetchDirection.RefetchAll, FetchTrigger.Manual));
            }
        }
    }

    /// <summary>Marks the pages out of date, regardless of <c>StaleTime</c>.</summary>
    public void Invalidate() => Invalidate(FetchTrigger.Invalidate, staleIfInFlight: true, respectStaleTime: false);

    /// <summary>Like <see cref="Invalidate()"/>, but a no-op while the pages are within their <c>StaleTime</c>.</summary>
    public void InvalidateIfStale() =>
        Invalidate(FetchTrigger.Invalidate, staleIfInFlight: true, respectStaleTime: true);

    /// <summary>
    /// The invalidation an observer issues when it attaches to this entry. Unlike <see cref="Invalidate()"/>
    /// it respects <c>StaleTime</c> and simply joins a run that is already in flight.
    /// </summary>
    internal void InvalidateOnAttach() =>
        Invalidate(FetchTrigger.Invalidate, staleIfInFlight: false, respectStaleTime: true);

    private void Invalidate(FetchTrigger trigger, bool staleIfInFlight, bool respectStaleTime)
    {
        if (respectStaleTime && IsFresh())
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            // A run in flight is never restarted by an invalidation (focus, reconnect, interval tick, a
            // mutation's InvalidateKeys): restarting on each one means a fetch slower than the
            // invalidation rate never completes, and it would drop a page fetch half way. The entry is
            // marked stale instead and refetched once the run settles, since that run may predate
            // whatever caused the invalidation. Refetch() still restarts it.
            if (_activeFetch is not null)
            {
                _isStale |= staleIfInFlight;
            }
            else if (_enabledSubscriberCount > 0)
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
            // Within StaleTime and not invalidated since: an invalidation marks pages stale however recent.
            return !_isStale && _lastSuccessAt is { } last && _scheduler.Now - last < _options.StaleTime;
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
        if (_disposed)
        {
            return;
        }

        var fetch = new object();
        CancellationTokenSource linkedSource;
        List<TData> snapshotPages;
        List<TPageParam> snapshotParams;
        bool snapshotHasNext;
        bool snapshotHasPrev;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _cancellationTokenSource.Token
            );
            snapshotPages = [.. _pages];
            snapshotParams = [.. _pageParams];
            snapshotHasNext = _state.Value.HasNextPage;
            snapshotHasPrev = _state.Value.HasPreviousPage;
            _activeFetch = fetch;

            // Only a full refetch brings the loaded pages up to date; a page fetch leaves them stale.
            if (command.Direction == FetchDirection.RefetchAll)
            {
                _isStale = false;
            }
        }

        using var cts = linkedSource;
        var linkedToken = cts.Token;

        var refetchParams =
            command.Direction != FetchDirection.RefetchAll ? null
            : snapshotParams.Count > 0 ? snapshotParams
            : [_options.InitialPageParam];

        using var activity = QueryTelemetry.ActivitySource.StartActivity(QueryTelemetryTags.ActivityQueryFetch);
        activity?.SetTag(QueryTelemetryTags.TagQueryKey, _key.ToString());
        activity?.SetTag(QueryTelemetryTags.TagQueryName, _name);
        activity?.SetTag(QueryTelemetryTags.TagTrigger, command.Trigger.ToTagValue());
        activity?.SetTag(QueryTelemetryTags.TagDirection, ToTagValue(command.Direction));

        var stopwatch = Stopwatch.StartNew();
        var counter = new AttemptCounter();

        _instrumentation.RecordFetchStart(_key, _name);

        try
        {
            var didFetch = command.Direction switch
            {
                FetchDirection.RefetchAll => await ExecuteRefetchAllAsync(
                        fetch,
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        refetchParams!,
                        activity,
                        counter,
                        linkedToken
                    )
                    .ConfigureAwait(false),
                FetchDirection.FetchNext => await ExecuteFetchNextAsync(
                        fetch,
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        command.PageParam,
                        activity,
                        counter,
                        linkedToken
                    )
                    .ConfigureAwait(false),
                FetchDirection.FetchPrevious => await ExecuteFetchPreviousAsync(
                        fetch,
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev,
                        command.PageParam,
                        activity,
                        counter,
                        linkedToken
                    )
                    .ConfigureAwait(false),
                _ => false,
            };

            stopwatch.Stop();

            if (!didFetch)
            {
                // The entry was disposed or the run superseded — balance RecordFetchStart without
                // reporting a successful fetch that never took effect.
                activity?.SetTag(QueryTelemetryTags.TagAttempts, counter.Attempts == 0 ? 0 : counter.Retries + 1);
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                _instrumentation.RecordFetchCancelled(_key, _name, stopwatch.Elapsed, command.Trigger);
                return;
            }

            var attempts = counter.Retries + 1;
            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagPages, refetchParams?.Count ?? 1);
            activity?.SetStatus(ActivityStatusCode.Ok);
            _instrumentation.RecordFetchSuccess(_key, _name, stopwatch.Elapsed, attempts, command.Trigger);
        }
        catch (Exception) when (linkedToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            activity?.SetTag(QueryTelemetryTags.TagAttempts, counter.Attempts == 0 ? 0 : counter.Retries + 1);
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            _instrumentation.RecordFetchCancelled(_key, _name, stopwatch.Elapsed, command.Trigger);

            // Only an explicit Cancel() rolls the state back. A run superseded by Switch() for a newer
            // command no longer owns the entry, so Settle publishes nothing for it — the newer run is
            // already driving _state.
            Settle(
                fetch,
                snapshotPages.Count > 0
                    ? InfiniteQueryState<TData, TPageParam>.CreateSuccess(
                        snapshotPages,
                        snapshotParams,
                        snapshotHasNext,
                        snapshotHasPrev
                    )
                    : InfiniteQueryState<TData, TPageParam>.CreateIdle()
            );
            return;
        }
        catch (Exception error)
        {
            stopwatch.Stop();

            var attempts = counter.Retries + 1;
            activity?.SetTag(QueryTelemetryTags.TagAttempts, attempts);
            activity?.SetTag(QueryTelemetryTags.TagErrorType, error.GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            _instrumentation.RecordFetchFailure(_key, _name, stopwatch.Elapsed, error, attempts, command.Trigger);

            Settle(
                fetch,
                InfiniteQueryState<TData, TPageParam>.CreateFailure(
                    error,
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev
                )
            );
        }

        // An invalidation that arrived while this run was in flight was held back; honour it now.
        lock (_syncRoot)
        {
            if (!_disposed && _isStale && _activeFetch is null && _enabledSubscriberCount > 0)
            {
                _command.OnNext(new FetchCommand(FetchDirection.RefetchAll, FetchTrigger.Invalidate));
            }
        }
    }

    /// <summary>Publishes a state of the run in progress, unless a newer run has taken over.</summary>
    private bool Publish(object fetch, InfiniteQueryState<TData, TPageParam> state)
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

    /// <summary>
    /// Ends a run and publishes its terminal state — atomically, so an invalidation can never slip a new
    /// run in between the two — unless a newer run has taken over, in which case nothing is committed or
    /// published. Passing <paramref name="pages"/> commits them as the loaded pages and stamps freshness.
    /// </summary>
    private bool Settle(
        object fetch,
        InfiniteQueryState<TData, TPageParam> state,
        List<TData>? pages = null,
        List<TPageParam>? pageParams = null
    )
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

            if (pages is not null && pageParams is not null)
            {
                _pages.Clear();
                _pages.AddRange(pages);
                _pageParams.Clear();
                _pageParams.AddRange(pageParams);
                _lastSuccessAt = _scheduler.Now;
            }

            _state.OnNext(state);
            return true;
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
        object fetch,
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
        if (
            !Publish(
                fetch,
                InfiniteQueryState<TData, TPageParam>.CreateFetching(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev
                )
            )
        )
        {
            return false;
        }

        var newPages = new List<TData>(paramsToFetch.Count);
        var newParams = new List<TPageParam>(paramsToFetch.Count);

        foreach (var param in paramsToFetch)
        {
            ct.ThrowIfCancellationRequested();
            var page = await FetchPageAsync(param, activity, counter, ct).ConfigureAwait(false);

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

        // A fetcher that ignores its token returns normally after being cancelled or superseded.
        ct.ThrowIfCancellationRequested();

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);

        return Settle(
            fetch,
            InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev),
            newPages,
            newParams
        );
    }

    private async Task<bool> ExecuteFetchNextAsync(
        object fetch,
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
        if (
            !Publish(
                fetch,
                InfiniteQueryState<TData, TPageParam>.CreateFetchingNext(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev
                )
            )
        )
        {
            return false;
        }

        var page = await FetchPageAsync(nextParam, activity, counter, ct).ConfigureAwait(false);

        // A fetcher that ignores its token returns normally after being cancelled or superseded.
        ct.ThrowIfCancellationRequested();

        // The run owns the entry from its snapshot until it settles, so the snapshot is still what is
        // loaded. Building the result from it leaves _pages untouched should anything below throw.
        List<TData> newPages = [.. snapshotPages, page];
        List<TPageParam> newParams = [.. snapshotParams, nextParam];

        if (_options.MaxPages is { } maxPages && newPages.Count > maxPages)
        {
            var removeCount = newPages.Count - maxPages;
            newPages.RemoveRange(0, removeCount);
            newParams.RemoveRange(0, removeCount);
        }

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);

        return Settle(
            fetch,
            InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev),
            newPages,
            newParams
        );
    }

    private async Task<bool> ExecuteFetchPreviousAsync(
        object fetch,
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
        if (
            !Publish(
                fetch,
                InfiniteQueryState<TData, TPageParam>.CreateFetchingPrevious(
                    snapshotPages,
                    snapshotParams,
                    snapshotHasNext,
                    snapshotHasPrev
                )
            )
        )
        {
            return false;
        }

        var page = await FetchPageAsync(prevParam, activity, counter, ct).ConfigureAwait(false);

        // A fetcher that ignores its token returns normally after being cancelled or superseded.
        ct.ThrowIfCancellationRequested();

        List<TData> newPages = [page, .. snapshotPages];
        List<TPageParam> newParams = [prevParam, .. snapshotParams];

        if (_options.MaxPages is { } maxPages && newPages.Count > maxPages)
        {
            var removeCount = newPages.Count - maxPages;
            newPages.RemoveRange(maxPages, removeCount);
            newParams.RemoveRange(maxPages, removeCount);
        }

        var (hasNext, hasPrev) = ComputeHasMorePages(newPages, newParams);

        return Settle(
            fetch,
            InfiniteQueryState<TData, TPageParam>.CreateSuccess(newPages, newParams, hasNext, hasPrev),
            newPages,
            newParams
        );
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

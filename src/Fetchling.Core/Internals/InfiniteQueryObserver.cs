namespace Fetchling.Core.Internals;

internal sealed class InfiniteQueryObserver<TArgs, TData, TPageParam> : IInfiniteQuery<TArgs, TData, TPageParam>
    where TData : class
{
    private readonly QueryCache _cache;
    private readonly EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly BehaviorSubject<InfiniteQuery<TArgs, TData, TPageParam>?> _activeQuery = new(null);
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _args = new();
    private readonly CompositeDisposable _subscriptions = [];

    private readonly Func<TArgs, QueryKey> _keyFactory;

    private QueryKey _currentKey = QueryKey.Default;
    private TArgs _currentArgs = default!;
    private bool _hasArgs;
    private bool _disposed;

    public InfiniteQueryObserver(
        InfiniteQueryOptions<TArgs, TData, TPageParam> options,
        QueryClientOptions globalOptions,
        QueryCache cache,
        IScheduler scheduler,
        QueryInstrumentation instrumentation
    )
    {
        _options = MergeOptions(options, globalOptions);
        _cache = cache;
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _isEnabled = new BehaviorSubject<bool>(options.IsEnabled);
        _keyFactory = options.KeyFactory;

        _subscriptions.Add(_args.Subscribe(Attach));

        _subscriptions.Add(
            _isEnabled
                .DistinctUntilChanged()
                .Where(enabled => enabled)
                .Subscribe(_ => _activeQuery.Value?.InvalidateOnAttach())
        );
    }

    private void Attach(TArgs args)
    {
        var key = _keyFactory(args);
        var candidate = new InfiniteQuery<TArgs, TData, TPageParam>(key, args, _options, _scheduler, _instrumentation);
        var query = _cache.GetOrCreate(key, candidate);

        if (!ReferenceEquals(query, candidate))
        {
            candidate.Dispose();
        }

        _currentKey = key;
        _currentArgs = args;
        _hasArgs = true;

        if (_isEnabled.Value)
        {
            query.InvalidateOnAttach();
        }

        _activeQuery.OnNext(query);
    }

    /// <summary>
    /// The cache entry for the current args. The cache evicts an entry once nothing has been subscribed
    /// to it for <c>CacheTime</c>, which can happen while this observer is still alive — so an evicted
    /// entry is replaced by attaching to the current args again, exactly as <see cref="SetArgs"/> would.
    /// </summary>
    private InfiniteQuery<TArgs, TData, TPageParam>? Active
    {
        get
        {
            var query = _activeQuery.Value;

            if (query is { IsDisposed: true } && _hasArgs && !_disposed && !_cache.IsDisposed)
            {
                Attach(_currentArgs);
                query = _activeQuery.Value;
            }

            return query;
        }
    }

    public QueryKey Key => _currentKey;

    public TimeSpan CacheTime => _options.CacheTime;

    public InfiniteQueryState<TData, TPageParam> CurrentState =>
        Active?.CurrentState ?? InfiniteQueryState<TData, TPageParam>.CreateIdle();

    public void SetArgs(TArgs args) => _args.OnNext(args);

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public void FetchNextPage() => Active?.FetchNextPage();

    public void FetchPreviousPage() => Active?.FetchPreviousPage();

    public IObservable<InfiniteQueryState<TData, TPageParam>> State =>
        Observable.Defer(() =>
        {
            // Replaces an evicted entry before subscribing to it; see Active.
            _ = Active;

            return _activeQuery.Where(query => query is not null).Select(query => query!.Observe(_isEnabled)).Switch();
        });

    public IObservable<IReadOnlyList<TData>> Success =>
        State
            .Where(s => s.IsSuccess && !s.IsFetchingNextPage && !s.IsFetchingPreviousPage)
            .Select(s => s.Pages)
            .DistinctUntilChanged(new PagesComparer(_options.DataComparer));

    public IObservable<Exception> Failure => State.Where(s => s.IsFailure).Select(s => s.Error!);

    public IObservable<InfiniteQueryState<TData, TPageParam>> Settled =>
        State.Where(s => s.IsFailure || (s.IsSuccess && !s.IsFetchingNextPage && !s.IsFetchingPreviousPage));

    public void Refetch() => Active?.Refetch();

    public void Cancel() => _activeQuery.Value?.Cancel();

    public void Invalidate() => Active?.Invalidate();

    public void Detach() => _cache.Remove(_currentKey);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscriptions.Dispose();
        _activeQuery.OnCompleted();
        _args.OnCompleted();
        _isEnabled.OnCompleted();
        _args.Dispose();
        _isEnabled.Dispose();
        _activeQuery.Dispose();
    }

    /// <summary>Compares two page lists element-wise using the query's <c>DataComparer</c>.</summary>
    private sealed class PagesComparer(IEqualityComparer<TData> pageComparer) : IEqualityComparer<IReadOnlyList<TData>>
    {
        public bool Equals(IReadOnlyList<TData>? x, IReadOnlyList<TData>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null || x.Count != y.Count)
            {
                return false;
            }

            for (var i = 0; i < x.Count; i++)
            {
                if (!pageComparer.Equals(x[i], y[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(IReadOnlyList<TData> obj) => obj.Count;
    }

    public static EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> MergeOptions(
        InfiniteQueryOptions<TArgs, TData, TPageParam> options,
        QueryClientOptions globalOptions
    ) =>
        new()
        {
            Fetcher = options.Fetcher,
            InitialPageParam = options.InitialPageParam,
            GetNextPageParam = options.GetNextPageParam,
            GetPreviousPageParam = options.GetPreviousPageParam,
            MaxPages = options.MaxPages,
            StaleTime = options.StaleTime ?? globalOptions.StaleTime,
            CacheTime = options.CacheTime ?? globalOptions.CacheTime,
            RefetchInterval = options.RefetchInterval ?? globalOptions.RefetchInterval,
            IsEnabled = options.IsEnabled,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            DataComparer = options.DataComparer ?? EqualityComparer<TData>.Default,
            InitialData = options.InitialData,
            Name = options.Name,
        };
}

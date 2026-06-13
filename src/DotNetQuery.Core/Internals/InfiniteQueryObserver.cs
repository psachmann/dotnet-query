namespace DotNetQuery.Core.Internals;

internal sealed class InfiniteQueryObserver<TArgs, TData, TPageParam>
    : IInfiniteQuery<TArgs, TData, TPageParam>,
        IQueryInspector
{
    private readonly QueryCache _cache;
    private readonly EffectiveInfiniteQueryOptions<TArgs, TData, TPageParam> _options;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly BehaviorSubject<InfiniteQuery<TArgs, TData, TPageParam>?> _activeQuery = new(null);
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _args = new();
    private readonly CompositeDisposable _subscriptions = [];

    private QueryKey _currentKey = QueryKey.Default;
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

        _subscriptions.Add(
            _args.Subscribe(args =>
            {
                var key = options.KeyFactory(args);
                var candidate = new InfiniteQuery<TArgs, TData, TPageParam>(
                    key,
                    args,
                    _options,
                    _scheduler,
                    _instrumentation
                );
                var query = _cache.GetOrCreate(key, candidate);

                if (!ReferenceEquals(query, candidate))
                {
                    candidate.Dispose();
                }

                _currentKey = key;

                if (_isEnabled.Value)
                {
                    query.Invalidate();
                }

                _activeQuery.OnNext(query);
            })
        );

        _subscriptions.Add(
            _isEnabled.DistinctUntilChanged().Where(enabled => enabled).Subscribe(_ => _activeQuery.Value?.Invalidate())
        );
    }

    public QueryKey Key => _currentKey;

    public TimeSpan CacheTime => _options.CacheTime;

    public InfiniteQueryState<TData, TPageParam> CurrentState =>
        _activeQuery.Value?.CurrentState ?? InfiniteQueryState<TData, TPageParam>.CreateIdle();

    public QueryStatus Status => CurrentState.Status;

    public object? CurrentData => _activeQuery.Value?.CurrentData;

    public DateTimeOffset? LastUpdatedAt => _activeQuery.Value?.LastUpdatedAt;

    public int ObserverCount => _activeQuery.Value?.ObserverCount ?? 0;

    public IObservable<Unit> StateChanged =>
        _activeQuery.Where(query => query is not null).Select(query => query!.StateChanged).Switch();

    public void SetArgs(TArgs args) => _args.OnNext(args);

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public void FetchNextPage() => _activeQuery.Value?.FetchNextPage();

    public void FetchPreviousPage() => _activeQuery.Value?.FetchPreviousPage();

    public IObservable<InfiniteQueryState<TData, TPageParam>> State =>
        _activeQuery.Where(query => query is not null).Select(query => query!.State).Switch();

    public IObservable<IReadOnlyList<TData>> Success =>
        State.Where(s => s.IsSuccess && !s.IsFetchingNextPage && !s.IsFetchingPreviousPage).Select(s => s.Pages);

    public IObservable<Exception> Failure => State.Where(s => s.IsFailure).Select(s => s.Error!);

    public IObservable<InfiniteQueryState<TData, TPageParam>> Settled =>
        State.Where(s => s.IsFailure || (s.IsSuccess && !s.IsFetchingNextPage && !s.IsFetchingPreviousPage));

    public void Refetch() => _activeQuery.Value?.Refetch();

    public void Cancel() => _activeQuery.Value?.Cancel();

    public void Invalidate() => _activeQuery.Value?.Invalidate();

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
        };
}

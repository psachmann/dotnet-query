namespace DotNetQuery.Core.Internals;

internal sealed class QueryObserver<TArgs, TData> : IQuery<TArgs, TData>, IQueryInspector
    where TData : class
{
    private readonly QueryCache _cache;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
    private readonly Func<TArgs, QueryKey> _keyFactory;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly BehaviorSubject<Query<TArgs, TData>?> _activeQuery = new(null);
    private readonly BehaviorSubject<bool> _isEnabled;
    private readonly Subject<TArgs> _args = new();
    private readonly CompositeDisposable _subscriptions = [];

    private QueryKey _currentKey = QueryKey.Default;
    private bool _disposed;

    public QueryObserver(
        QueryOptions<TArgs, TData> options,
        QueryClientOptions globalOptions,
        QueryCache cache,
        IScheduler scheduler,
        QueryInstrumentation instrumentation
    )
    {
        _options = MergeOptions(options, globalOptions);
        _keyFactory = options.KeyFactory;
        _cache = cache;
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _isEnabled = new BehaviorSubject<bool>(options.IsEnabled);

        _subscriptions.Add(
            _args.Subscribe(args =>
            {
                var key = options.KeyFactory(args);
                var candidate = new Query<TArgs, TData>(key, args, _options, _scheduler, _instrumentation);
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

    public QueryState<TData> CurrentState => _activeQuery.Value?.CurrentState ?? QueryState<TData>.CreateIdle();

    public QueryStatus Status => CurrentState.Status;

    public object? CurrentData => CurrentState.CurrentData;

    public DateTimeOffset? LastUpdatedAt => _activeQuery.Value?.LastUpdatedAt;

    public int ObserverCount => _activeQuery.Value?.ObserverCount ?? 0;

    public IObservable<Unit> StateChanged =>
        _activeQuery.Where(query => query is not null).Select(query => query!.StateChanged).Switch();

    public void SetArgs(TArgs args) => _args.OnNext(args);

    public void SetData(TData data) => _activeQuery.Value?.SetData(data);

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public IObservable<QueryState<TData>> State =>
        _activeQuery.Where(query => query is not null).Select(query => query!.State).Switch();

    public IObservable<TData> Success =>
        State
            .Where(state => state.IsSuccess)
            .Select(state => state.CurrentData!)
            .DistinctUntilChanged(_options.DataComparer);

    public IObservable<Exception> Failure => State.Where(state => state.IsFailure).Select(state => state.Error!);

    public IObservable<QueryState<TData>> Settled => State.Where(state => state.IsSuccess || state.IsFailure);

    public IObservable<TResult> Select<TResult>(
        Func<TData, TResult> selector,
        IEqualityComparer<TResult>? comparer = null
    ) => Success.Select(selector).DistinctUntilChanged(comparer ?? EqualityComparer<TResult>.Default);

    public void Refetch() => _activeQuery.Value?.Refetch();

    public void Cancel() => _activeQuery.Value?.Cancel();

    public void Invalidate() => _activeQuery.Value?.Invalidate();

    public void Detach() => _cache.Remove(_currentKey);

    public Task PrefetchAsync(TArgs args, CancellationToken cancellationToken = default)
    {
        var key = _keyFactory(args);
        var candidate = new Query<TArgs, TData>(key, args, _options, _scheduler, _instrumentation);
        var query = _cache.GetOrCreate(key, candidate);

        if (!ReferenceEquals(query, candidate))
        {
            candidate.Dispose();
        }

        return query.PrefetchAsync(cancellationToken);
    }

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

    public static EffectiveQueryOptions<TArgs, TData> MergeOptions(
        QueryOptions<TArgs, TData> options,
        QueryClientOptions globalOptions
    )
    {
        return new()
        {
            Fetcher = options.Fetcher,
            StaleTime = options.StaleTime ?? globalOptions.StaleTime,
            CacheTime = options.CacheTime ?? globalOptions.CacheTime,
            RefetchInterval = options.RefetchInterval ?? globalOptions.RefetchInterval,
            IsEnabled = options.IsEnabled,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            DataComparer = options.DataComparer ?? EqualityComparer<TData>.Default,
            InitialData = options.InitialData,
        };
    }
}

namespace DotNetQuery.Core.Internals;

internal sealed class QueryObserver<TArgs, TData> : IQuery<TArgs, TData>
{
    private readonly QueryCache _cache;
    private readonly EffectiveQueryOptions<TArgs, TData> _options;
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
        _cache = cache;
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _isEnabled = new BehaviorSubject<bool>(options.IsEnabled);

        _subscriptions.Add(
            _args.Subscribe(args =>
            {
                var key = options.KeyFactory(args);
                var query = _cache.GetOrCreate(
                    key,
                    new Query<TArgs, TData>(key, args, _options, _scheduler, _instrumentation)
                );

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

    public void SetArgs(TArgs args) => _args.OnNext(args);

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public IObservable<QueryState<TData>> State =>
        _activeQuery.Where(query => query is not null).Select(query => query!.State).Switch();

    public IObservable<TData> Success => State.Where(state => state.IsSuccess).Select(state => state.CurrentData!);

    public IObservable<Exception> Failure => State.Where(state => state.IsFailure).Select(state => state.Error!);

    public IObservable<QueryState<TData>> Settled => State.Where(state => state.IsSuccess || state.IsFailure);

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
        };
    }
}

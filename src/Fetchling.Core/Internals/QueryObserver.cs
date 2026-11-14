namespace Fetchling.Core.Internals;

internal sealed class QueryObserver<TArgs, TData> : IQuery<TArgs, TData>
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
    private TArgs _currentArgs = default!;
    private bool _hasArgs;
    private bool _disposed;

    public QueryObserver(
        QueryOptions<TArgs, TData> options,
        QueryClientOptions globalOptions,
        QueryCache cache,
        IScheduler scheduler,
        QueryInstrumentation instrumentation
    )
        : this(MergeOptions(options, globalOptions), options.KeyFactory, cache, scheduler, instrumentation) { }

    public QueryObserver(
        EffectiveQueryOptions<TArgs, TData> options,
        Func<TArgs, QueryKey> keyFactory,
        QueryCache cache,
        IScheduler scheduler,
        QueryInstrumentation instrumentation
    )
    {
        _options = options;
        _keyFactory = keyFactory;
        _cache = cache;
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _isEnabled = new BehaviorSubject<bool>(options.IsEnabled);

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
        var candidate = new Query<TArgs, TData>(key, args, _options, _scheduler, _instrumentation);
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
    private Query<TArgs, TData>? Active
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

    public QueryState<TData> CurrentState => Active?.CurrentState ?? QueryState<TData>.CreateIdle();

    public void SetArgs(TArgs args) => _args.OnNext(args);

    public void SetData(TData data) => Active?.SetData(data);

    public void SetEnabled(bool enabled) => _isEnabled.OnNext(enabled);

    public IObservable<QueryState<TData>> State =>
        Observable.Defer(() =>
        {
            // Replaces an evicted entry before subscribing to it; see Active.
            _ = Active;

            return _activeQuery.Where(query => query is not null).Select(query => query!.Observe(_isEnabled)).Switch();
        });

    public IObservable<TData> Success =>
        State
            .Where(state => state.IsSuccess && !state.IsStreaming)
            .Select(state => state.CurrentData!)
            .DistinctUntilChanged(_options.DataComparer);

    public IObservable<Exception> Failure => State.Where(state => state.IsFailure).Select(state => state.Error!);

    public IObservable<QueryState<TData>> Settled =>
        State.Where(state => (state.IsSuccess && !state.IsStreaming) || state.IsFailure);

    public IObservable<TResult> Select<TResult>(
        Func<TData, TResult> selector,
        IEqualityComparer<TResult>? comparer = null
    ) => Success.Select(selector).DistinctUntilChanged(comparer ?? EqualityComparer<TResult>.Default);

    public void Refetch() => Active?.Refetch();

    public void Cancel() => _activeQuery.Value?.Cancel();

    public void Invalidate() => Active?.Invalidate();

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
            Name = options.Name,
        };
    }

    public static EffectiveQueryOptions<TArgs, TData> MergeOptions<TChunk>(
        StreamedQueryOptions<TArgs, TData, TChunk> options,
        QueryClientOptions globalOptions
    )
    {
        var stream = StreamedFetch<TArgs, TData>.Create(options);

        return new()
        {
            // Query uses Stream whenever it is set; this folds the whole stream without publishing chunks,
            // so the options stay a valid regular-query configuration too.
            Fetcher = (args, ct) => stream.FetchAsync(args, static _ => { }, ct),
            Stream = stream,
            StaleTime = options.StaleTime ?? globalOptions.StaleTime,
            CacheTime = options.CacheTime ?? globalOptions.CacheTime,
            // Deliberately no fallback to the global interval — see StreamedQueryOptions.RefetchInterval.
            RefetchInterval = options.RefetchInterval,
            IsEnabled = options.IsEnabled,
            RetryHandler = options.RetryHandler ?? globalOptions.RetryHandler,
            DataComparer = options.DataComparer ?? EqualityComparer<TData>.Default,
            InitialData = null,
            Name = options.Name,
        };
    }
}

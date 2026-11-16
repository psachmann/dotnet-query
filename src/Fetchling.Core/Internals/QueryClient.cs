namespace Fetchling.Core.Internals;

internal sealed class QueryClient : IQueryClient, IQueryCacheInspector
{
    private readonly QueryCache _cache;
    private readonly QueryClientOptions _globalOptions;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;

    public QueryClient(QueryClientOptions globalOptions, IScheduler scheduler, QueryInstrumentation instrumentation)
    {
        _globalOptions = globalOptions;
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _cache = new(_scheduler, _instrumentation);
    }

    public IObservable<IReadOnlyList<IQueryCacheEntry>> Entries => _cache.Entries;

    public IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options)
        where TData : class =>
        new QueryObserver<TArgs, TData>(options, _globalOptions, _cache, _scheduler, _instrumentation);

    public IInfiniteQuery<TArgs, TData, TPageParam> CreateInfiniteQuery<TArgs, TData, TPageParam>(
        InfiniteQueryOptions<TArgs, TData, TPageParam> options
    )
        where TData : class =>
        new InfiniteQueryObserver<TArgs, TData, TPageParam>(
            options,
            _globalOptions,
            _cache,
            _scheduler,
            _instrumentation
        );

    public IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(
        StreamedQueryOptions<TArgs, TData, TChunk> options
    )
        where TData : class =>
        new QueryObserver<TArgs, TData>(
            QueryObserver<TArgs, TData>.MergeOptions(options, _globalOptions),
            options.KeyFactory,
            _cache,
            _scheduler,
            _instrumentation
        );

    public IMutation<TArgs, TData> CreateMutation<TArgs, TData>(MutationOptions<TArgs, TData> options)
    {
        var mutation = new Mutation<TArgs, TData>(options, _globalOptions, _instrumentation);
        InvalidateOnSuccess(mutation, options.InvalidateKeys);

        return mutation;
    }

    public IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(
        MutationOptions<TArgs, TData, TProgress> options
    )
    {
        var mutation = new Mutation<TArgs, TData, TProgress>(options, _globalOptions, _instrumentation);
        InvalidateOnSuccess(mutation, options.InvalidateKeys);

        return mutation;
    }

    public void Invalidate(QueryKey key) => _cache.Invalidate(key);

    public void Invalidate(Func<QueryKey, bool> predicate) => _cache.Invalidate(predicate);

    public bool SetData<TData>(QueryKey key, TData data)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);

        return GetWritable<TData>(key)?.TryWrite(_ => data) ?? false;
    }

    public bool SetData<TData>(QueryKey key, Func<TData, TData> updater)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(updater);

        return GetWritable<TData>(key)?.TryWrite(current => current is null ? null : updater(current)) ?? false;
    }

    public int SetData<TData>(Func<QueryKey, bool> predicate, Func<TData, TData> updater)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(updater);

        var written = 0;

        foreach (var entry in _cache.Match(predicate))
        {
            if (
                entry is IWritableEntry<TData> writable
                && writable.TryWrite(current => current is null ? null : updater(current))
            )
            {
                written++;
            }
        }

        return written;
    }

    private IWritableEntry<TData>? GetWritable<TData>(QueryKey key)
        where TData : class
    {
        if (!_cache.TryGet(key, out var entry))
        {
            return null;
        }

        return entry as IWritableEntry<TData>
            ?? throw new InvalidOperationException(
                $"QueryKey '{key}' is held by a query with a different shape ({entry.GetType().Name}), so its "
                    + $"data cannot be written as {typeof(TData).Name}. Use the data type the query was created "
                    + "with; an infinite query's pages cannot be written this way."
            );
    }

    public void Dispose() => _cache.Dispose();

    private void InvalidateOnSuccess<TArgs, TData, TProgress, TState>(
        MutationCore<TArgs, TData, TProgress, TState> mutation,
        IReadOnlyList<QueryKey>? keys
    )
        where TState : class, IMutationState<TData>
    {
        if (keys is not { Count: > 0 })
        {
            return;
        }

        var subscription = mutation.Success.Subscribe(_ =>
        {
            foreach (var key in keys)
            {
                Invalidate(key);
            }
        });

        mutation.AddDisposable(subscription);
    }
}

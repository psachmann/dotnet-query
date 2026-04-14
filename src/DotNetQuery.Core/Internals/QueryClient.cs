namespace DotNetQuery.Core.Internals;

internal sealed class QueryClient : IQueryClient
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

    public IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options) =>
        new QueryObserver<TArgs, TData>(options, _globalOptions, _cache, _scheduler, _instrumentation);

    public IMutation<TArgs, TData> CreateMutation<TArgs, TData>(MutationOptions<TArgs, TData> options)
    {
        var mutation = new Mutation<TArgs, TData>(options, _globalOptions, _instrumentation);

        if (options.InvalidateKeys is { Count: > 0 } keys)
        {
            var subscription = mutation.Success.Subscribe(_ =>
            {
                foreach (var key in keys)
                {
                    Invalidate(key);
                }
            });

            mutation.AddDisposable(subscription);
        }

        return mutation;
    }

    public Task PrefetchQueryAsync<TArgs, TData>(
        TArgs args,
        QueryOptions<TArgs, TData> options,
        CancellationToken cancellationToken = default
    )
    {
        var mergedOptions = QueryObserver<TArgs, TData>.MergeOptions(options, _globalOptions);
        var key = options.KeyFactory(args);
        var candidate = new Query<TArgs, TData>(key, args, mergedOptions, _scheduler, _instrumentation);
        var query = _cache.GetOrCreate(key, candidate);

        if (!ReferenceEquals(query, candidate))
        {
            candidate.Dispose();
        }

        return query.PrefetchAsync(cancellationToken);
    }

    public void Invalidate(QueryKey key) => _cache.Invalidate(key);

    public void Invalidate(Func<QueryKey, bool> predicate) => _cache.Invalidate(predicate);

    public void Dispose() => _cache.Dispose();
}

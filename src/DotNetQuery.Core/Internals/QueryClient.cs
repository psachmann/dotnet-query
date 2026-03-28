namespace DotNetQuery.Core.Internals;

internal sealed class QueryClient : IQueryClient
{
    private readonly QueryCache _cache;
    private readonly QueryClientOptions _globalOptions;
    private readonly IScheduler _scheduler;

    public QueryClient(QueryClientOptions globalOptions, IScheduler? scheduler = null)
    {
        _globalOptions = globalOptions;
        _scheduler = scheduler ?? Scheduler.Default;
        _cache = new QueryCache(scheduler);
    }

    public IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options) =>
        new QueryObserver<TArgs, TData>(options, _globalOptions, _cache, _scheduler);

    public IMutation<TArgs, TData> CreateMutation<TArgs, TData>(MutationOptions<TArgs, TData> options)
    {
        var mutation = new Mutation<TArgs, TData>(options);

        if (options.InvalidateKeys is { Count: > 0 } keys)
        {
            mutation.Success.Subscribe(_ =>
            {
                foreach (var key in keys)
                    Invalidate(key);
            });
        }

        return mutation;
    }

    public void Invalidate(QueryKey key) => _cache.Invalidate(key);

    public void Invalidate(Func<QueryKey, bool> predicate) => _cache.Invalidate(predicate);

    public void Dispose() => _cache.Dispose();
}

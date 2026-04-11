namespace DotNetQuery.Core.Internals;

internal sealed class QueryCache(IScheduler scheduler, QueryInstrumentation instrumentation) : IDisposable
{
    private readonly ConcurrentDictionary<QueryKey, IQuery> _entries = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _pendingRemovals = new();
    private readonly IScheduler _scheduler = scheduler;
    private readonly QueryInstrumentation _instrumentation = instrumentation;
    private readonly Lock _evictionLock = new();

    public Query<TArgs, TData> GetOrCreate<TArgs, TData>(QueryKey key, Query<TArgs, TData> query)
    {
        lock (_evictionLock)
        {
            if (_pendingRemovals.TryRemove(key, out var pending))
            {
                pending.Dispose();
            }

            var result = (Query<TArgs, TData>)_entries.GetOrAdd(key, query);

            if (ReferenceEquals(result, query))
            {
                _instrumentation.RecordCacheMiss(key);
            }
            else
            {
                _instrumentation.RecordCacheHit(key);
            }

            return result;
        }
    }

    public void Remove(QueryKey key)
    {
        if (!_entries.TryGetValue(key, out var query))
        {
            return;
        }

        var subscription = Observable
            .Timer(query.CacheTime, _scheduler)
            .Subscribe(_ =>
            {
                IQuery? toDispose = null;

                lock (_evictionLock)
                {
                    if (_pendingRemovals.TryRemove(key, out IDisposable? _) && _entries.TryRemove(key, out var query))
                    {
                        toDispose = query;
                    }
                }

                toDispose?.Dispose();
            });

        _pendingRemovals[key] = subscription;
    }

    public void Invalidate(QueryKey key)
    {
        if (_entries.TryGetValue(key, out var query))
        {
            query.Invalidate();
        }
    }

    public void Invalidate(Func<QueryKey, bool> predicate)
    {
        foreach (var key in _entries.Keys.ToList())
        {
            if (predicate(key) && _entries.TryGetValue(key, out var query))
            {
                query.Invalidate();
            }
        }
    }

    public void Dispose()
    {
        foreach (var subscription in _pendingRemovals.Values)
        {
            subscription.Dispose();
        }

        _pendingRemovals.Clear();

        foreach (var query in _entries.Values)
        {
            query.Dispose();
        }

        _entries.Clear();
    }
}

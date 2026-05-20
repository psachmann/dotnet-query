namespace DotNetQuery.Core.Internals;

internal sealed class QueryCache : IDisposable
{
    private readonly ConcurrentDictionary<QueryKey, IQuery> _entries = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _pendingRemovals = new();
    private readonly BehaviorSubject<IReadOnlyDictionary<QueryKey, IQuery>> _entriesSubject;
    private readonly IScheduler _scheduler;
    private readonly QueryInstrumentation _instrumentation;
    private readonly Lock _evictionLock = new();
    private bool _disposed;

    public QueryCache(IScheduler scheduler, QueryInstrumentation instrumentation)
    {
        _scheduler = scheduler;
        _instrumentation = instrumentation;
        _entriesSubject = new(_entries);
    }

    public IObservable<IReadOnlyDictionary<QueryKey, IQuery>> Entries => _entriesSubject.AsObservable();

    public Query<TArgs, TData> GetOrCreate<TArgs, TData>(QueryKey key, Query<TArgs, TData> query)
    {
        lock (_evictionLock)
        {
            if (_pendingRemovals.TryRemove(key, out var pending))
            {
                pending.Dispose();
                _entriesSubject.OnNext(_entries);
            }

            var result = (Query<TArgs, TData>)_entries.GetOrAdd(key, query);

            if (ReferenceEquals(result, query))
            {
                _instrumentation.RecordCacheMiss(key);
                _entriesSubject.OnNext(_entries);
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
                _entriesSubject.OnNext(_entries);
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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
        _entriesSubject.OnCompleted();
        _entriesSubject.Dispose();
    }
}

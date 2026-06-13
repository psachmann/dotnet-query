namespace DotNetQuery.Core.Internals;

internal sealed class QueryCache(IScheduler scheduler, QueryInstrumentation instrumentation) : IDisposable
{
    private readonly ConcurrentDictionary<QueryKey, IQuery> _entries = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _pendingRemovals = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _stateSubscriptions = new();
    private readonly BehaviorSubject<IReadOnlyList<IQueryInspector>> _entriesSubject = new([]);
    private readonly IScheduler _scheduler = scheduler;
    private readonly QueryInstrumentation _instrumentation = instrumentation;
    private readonly Lock _evictionLock = new();
    private bool _disposed;

    public IObservable<IReadOnlyList<IQueryInspector>> Entries => _entriesSubject.AsObservable();

    private IReadOnlyList<IQueryInspector> Snapshot() => [.. _entries.Values.Cast<IQueryInspector>()];

    public TEntry GetOrCreate<TEntry>(QueryKey key, TEntry entry)
        where TEntry : class, IQueryInspector
    {
        lock (_evictionLock)
        {
            if (_pendingRemovals.TryRemove(key, out var pending))
            {
                pending.Dispose();
                _entriesSubject.OnNext(Snapshot());
            }

            var result = (TEntry)_entries.GetOrAdd(key, entry);

            if (ReferenceEquals(result, entry))
            {
                _instrumentation.RecordCacheMiss(key);
                _stateSubscriptions[key] = entry.StateChanged.Subscribe(_ => _entriesSubject.OnNext(Snapshot()));
                _entriesSubject.OnNext(Snapshot());
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
                    if (_pendingRemovals.TryRemove(key, out IDisposable? _) && _entries.TryRemove(key, out var removed))
                    {
                        toDispose = removed;
                        if (_stateSubscriptions.TryRemove(key, out var stateSub))
                        {
                            stateSub.Dispose();
                        }
                    }
                }

                toDispose?.Dispose();
                _entriesSubject.OnNext(Snapshot());
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

        foreach (var subscription in _stateSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _stateSubscriptions.Clear();

        foreach (var query in _entries.Values)
        {
            query.Dispose();
        }

        _entries.Clear();
        _entriesSubject.OnCompleted();
        _entriesSubject.Dispose();
    }
}

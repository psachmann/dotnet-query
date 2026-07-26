namespace DotNetQuery.Core.Internals;

internal sealed class QueryCache(IScheduler scheduler, QueryInstrumentation instrumentation) : IDisposable
{
    private readonly ConcurrentDictionary<QueryKey, IQuery> _entries = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _pendingRemovals = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _stateSubscriptions = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _unsubscribedSubscriptions = new();
    private readonly ConcurrentDictionary<QueryKey, IDisposable> _subscribedSubscriptions = new();
    private readonly BehaviorSubject<IReadOnlyList<IQueryInspector>> _entriesSubject = new([]);
    private readonly IScheduler _scheduler = scheduler;
    private readonly QueryInstrumentation _instrumentation = instrumentation;
    private readonly Lock _evictionLock = new();
    private bool _disposed;

    public IObservable<IReadOnlyList<IQueryInspector>> Entries => _entriesSubject.AsObservable();

    private IReadOnlyList<IQueryInspector> Snapshot() => [.. _entries.Values.Cast<IQueryInspector>()];

    public Query<TArgs, TData> GetOrCreate<TArgs, TData>(QueryKey key, Query<TArgs, TData> query)
        where TData : class
    {
        Query<TArgs, TData> result;
        var changed = false;

        lock (_evictionLock)
        {
            // Cancelling and looking up/adding the entry must happen atomically under the same lock
            // acquisition — otherwise a concurrent Remove(key) could schedule an eviction in the gap
            // between the two, after this call has already attached a new observer to the entry.
            if (_pendingRemovals.TryRemove(key, out var pending))
            {
                pending.Dispose();
                changed = true;
            }

            var existing = _entries.GetOrAdd(key, query);

            if (existing is not Query<TArgs, TData> typed)
            {
                throw new InvalidOperationException(
                    $"QueryKey '{key}' is already in use by a query with a different data type "
                        + $"({existing.GetType().Name}). Use a distinct QueryKey per (TArgs, TData) combination."
                );
            }

            result = typed;

            if (ReferenceEquals(result, query))
            {
                _instrumentation.RecordCacheMiss(key);
                _stateSubscriptions[key] = query.StateChanged.Subscribe(_ => _entriesSubject.OnNext(Snapshot()));
                _unsubscribedSubscriptions[key] = query.Unsubscribed.Subscribe(_ => Remove(key));
                _subscribedSubscriptions[key] = query.Subscribed.Subscribe(_ => CancelPendingRemoval(key));
                changed = true;
            }
            else
            {
                _instrumentation.RecordCacheHit(key);
            }
        }

        if (changed)
        {
            _entriesSubject.OnNext(Snapshot());
        }

        return result;
    }

    /// <summary>
    /// Cancels a pending eviction for <paramref name="key"/>, if any — e.g. because a new observer
    /// attached (via <see cref="GetOrCreate{TArgs,TData}"/>) or a new <c>State</c> subscriber joined
    /// a query that was already counting down to eviction.
    /// </summary>
    private void CancelPendingRemoval(QueryKey key)
    {
        bool cancelled;

        lock (_evictionLock)
        {
            cancelled = _pendingRemovals.TryRemove(key, out var pending);
            if (cancelled)
            {
                pending!.Dispose();
            }
        }

        if (cancelled)
        {
            _entriesSubject.OnNext(Snapshot());
        }
    }

    public void Remove(QueryKey key)
    {
        lock (_evictionLock)
        {
            if (!_entries.TryGetValue(key, out var query))
            {
                return;
            }

            IDisposable subscription;

            try
            {
                subscription = Observable
                    .Timer(query.CacheTime, _scheduler)
                    .Subscribe(_ =>
                    {
                        IQuery? toDispose = null;

                        lock (_evictionLock)
                        {
                            if (
                                _pendingRemovals.TryRemove(key, out IDisposable? _)
                                && _entries.TryRemove(key, out var removed)
                            )
                            {
                                toDispose = removed;
                                if (_stateSubscriptions.TryRemove(key, out var stateSub))
                                {
                                    stateSub.Dispose();
                                }
                                if (_unsubscribedSubscriptions.TryRemove(key, out var unsubscribedSub))
                                {
                                    unsubscribedSub.Dispose();
                                }
                                if (_subscribedSubscriptions.TryRemove(key, out var subscribedSub))
                                {
                                    subscribedSub.Dispose();
                                }
                            }
                        }

                        toDispose?.Dispose();
                        _entriesSubject.OnNext(Snapshot());
                    });
            }
            catch (ArgumentOutOfRangeException)
            {
                // CacheTime exceeds what the scheduler's underlying timer can represent (e.g. DefaultScheduler's
                // ~49-day System.Threading.Timer limit) — treat it as "never evict" instead of crashing.
                return;
            }

            if (_pendingRemovals.TryGetValue(key, out var existing))
            {
                existing.Dispose();
            }

            _pendingRemovals[key] = subscription;
        }
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

        foreach (var subscription in _unsubscribedSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _unsubscribedSubscriptions.Clear();

        foreach (var subscription in _subscribedSubscriptions.Values)
        {
            subscription.Dispose();
        }

        _subscribedSubscriptions.Clear();

        foreach (var query in _entries.Values)
        {
            query.Dispose();
        }

        _entries.Clear();
        _entriesSubject.OnCompleted();
        _entriesSubject.Dispose();
    }
}

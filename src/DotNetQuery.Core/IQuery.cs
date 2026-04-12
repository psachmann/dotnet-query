namespace DotNetQuery.Core;

/// <summary>
/// Base interface for a query. Provides lifecycle management and key access.
/// </summary>
public interface IQuery : IDisposable
{
    /// <summary>
    /// The key that uniquely identifies this query in the client cache.
    /// Returns <see cref="QueryKey.Default"/> before args have been set for the first time.
    /// Check against <see cref="QueryKey.Default"/> to detect the uninitialized state.
    /// </summary>
    public QueryKey Key { get; }

    /// <summary>
    /// The duration for which fetched data is kept in the cache after all subscribers have disposed.
    /// Once elapsed, the cache entry is evicted.
    /// </summary>
    public TimeSpan CacheTime { get; }

    /// <summary>
    /// Triggers an immediate re-fetch using the current args, bypassing stale-time checks.
    /// </summary>
    public void Refetch();

    /// <summary>
    /// Cancels the currently running fetch, if any.
    /// </summary>
    public void Cancel();

    /// <summary>
    /// Marks the cached data as stale. If there are active subscribers, a re-fetch is triggered
    /// immediately; otherwise the fetch is deferred until the first subscriber joins.
    /// <para>
    /// If the last successful fetch occurred within the configured <c>StaleTime</c> window, this call
    /// is a no-op. Use <see cref="Refetch"/> to bypass stale-time and force an immediate fetch.
    /// </para>
    /// </summary>
    public void Invalidate();
}

/// <summary>
/// A query that fetches data using the provided <typeparamref name="TArgs"/>.
/// Inherits all lifecycle members from <see cref="IQuery"/>.
/// </summary>
/// <typeparam name="TArgs">The type of the parameters passed to the fetcher.</typeparam>
/// <typeparam name="TData">The type of the data returned by the fetcher.</typeparam>
public interface IQuery<TArgs, TData> : IQuery
{
    /// <summary>
    /// The current state snapshot. Can be read synchronously without subscribing.
    /// </summary>
    public QueryState<TData> CurrentState { get; }

    /// <summary>
    /// Sets the args that drive the next fetch. When args change, the query switches to the
    /// cache entry for the newly derived key and triggers a fetch if the query is enabled.
    /// </summary>
    public void SetArgs(TArgs args);

    /// <summary>
    /// Enables or disables the query. When <c>false</c>, fetching is suspended even if the query is
    /// invalidated or new args are pushed. Pass <c>true</c> to resume; the query will immediately
    /// re-evaluate its active key and trigger a fetch if one is pending.
    /// </summary>
    public void SetEnabled(bool enabled);

    /// <summary>
    /// Emits on every state transition (e.g. Idle → Fetching → Success/Failed).
    /// Replays the latest state to new subscribers.
    /// </summary>
    public IObservable<QueryState<TData>> State { get; }

    /// <summary>
    /// Emits the unwrapped <typeparamref name="TData"/> on each successful fetch.
    /// </summary>
    public IObservable<TData> Success { get; }

    /// <summary>
    /// Emits the <see cref="Exception"/> on each failed fetch.
    /// </summary>
    public IObservable<Exception> Failure { get; }

    /// <summary>
    /// Emits the final <see cref="QueryState{TData}"/> after each fetch completes,
    /// regardless of whether it succeeded or failed. Useful for hiding loading indicators.
    /// </summary>
    public IObservable<QueryState<TData>> Settled { get; }

    /// <summary>
    /// Removes this query from the client cache while keeping active subscriptions alive.
    /// Use <see cref="IDisposable.Dispose"/> to also tear down all subscriptions and release resources.
    /// </summary>
    public void Detach();

    /// <summary>
    /// Returns a derived observable that applies <paramref name="selector"/> to each successfully fetched value
    /// and suppresses re-emissions when the selected result is equal according to <paramref name="comparer"/>
    /// (defaults to <see cref="EqualityComparer{T}.Default"/>).
    /// The underlying fetch and cache are unaffected — no additional fetches are triggered.
    /// </summary>
    /// <typeparam name="TResult">The type produced by the selector.</typeparam>
    /// <param name="selector">A transform applied to each successful <typeparamref name="TData"/> value.</param>
    /// <param name="comparer">
    /// Equality comparer for <typeparamref name="TResult"/>. When <c>null</c>,
    /// <see cref="EqualityComparer{T}.Default"/> is used.
    /// </param>
    public IObservable<TResult> Select<TResult>(
        Func<TData, TResult> selector,
        IEqualityComparer<TResult>? comparer = null
    );
}

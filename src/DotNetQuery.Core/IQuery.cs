namespace DotNetQuery.Core;

/// <summary>
/// Base interface for a query. Provides lifecycle management and key access.
/// </summary>
public interface IQuery : IDisposable
{
    /// <summary>
    /// The key that uniquely identifies this query in the client cache.
    /// Returns <c>null</c> before args have been set for the first time.
    /// </summary>
    public QueryKey Key { get; }

    public TimeSpan CacheTime { get; }

    /// <summary>
    /// Triggers an immediate re-fetch using the current args, bypassing stale-time checks.
    /// </summary>
    public void Refetch();

    public void Cancel();

    /// <summary>
    /// Marks the cached data as stale. The next access or active subscriber will trigger a re-fetch.
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
    /// Push new args to trigger a fetch. Use <see cref="IObserver{T}.OnNext"/> to update.
    /// When args change, the observer switches to the cache entry for the new derived key.
    /// </summary>
    public IObserver<TArgs> Args { get; }

    /// <summary>
    /// Controls whether the query is active. Emit <c>false</c> to suspend fetching, <c>true</c> to resume.
    /// The query will not fetch while disabled, even if invalidated or args change.
    /// </summary>
    public IObserver<bool> IsEnabled { get; }

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
}

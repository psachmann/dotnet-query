namespace DotNetQuery.Core.Internals;

/// <summary>
/// A query that can be stored in the <see cref="QueryCache"/>. Extends the inspection surface with the
/// subscriber-lifecycle signals the cache uses to drive eviction. Implemented by the cached entries
/// themselves (<see cref="Query{TArgs,TData}"/>, <see cref="InfiniteQuery{TArgs,TData,TPageParam}"/>) —
/// not by the observers handed back to callers.
/// </summary>
internal interface ICacheEntry : IQueryInspector
{
    /// <summary>Fires when the last active <c>State</c> subscriber disposes.</summary>
    public IObservable<Unit> Unsubscribed { get; }

    /// <summary>Fires when the first <c>State</c> subscriber attaches after having none.</summary>
    public IObservable<Unit> Subscribed { get; }
}

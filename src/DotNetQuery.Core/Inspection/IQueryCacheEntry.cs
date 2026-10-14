namespace DotNetQuery.Core.Inspection;

/// <summary>
/// A read-only view of one entry in a query client's cache — a regular or an infinite query — plus the
/// actions that are safe to invoke on an entry the cache owns. Obtained from
/// <see cref="IQueryCacheInspector.Entries"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a <b>live</b> view, not a snapshot: every property reads the entry's state at the moment it is
/// accessed. Read the properties when handling an <see cref="IQueryCacheInspector.Entries"/> emission and
/// copy what you need, rather than holding on to the entry and reading it later from another thread.
/// </para>
/// <para>
/// The cache owns the entry's lifetime. There is deliberately no way to dispose it from here — an entry is
/// evicted once it has had no subscribers for its <see cref="CacheTime"/>.
/// </para>
/// </remarks>
public interface IQueryCacheEntry
{
    /// <summary>The key that identifies this entry in the cache.</summary>
    public QueryKey Key { get; }

    /// <summary>
    /// A low-cardinality, human-readable name for the entry: the query's <c>Name</c> option when set,
    /// otherwise the first part of <see cref="Key"/>, otherwise <c>"unknown"</c>. This is the same value
    /// that tags the entry's metrics.
    /// </summary>
    public string Name { get; }

    /// <summary>The current lifecycle status of the entry.</summary>
    public QueryStatus Status { get; }

    /// <summary>
    /// The data held by the entry, untyped. For a regular query this is the data of the latest successful
    /// fetch, and <c>null</c> while a fetch is in progress or after a failure — see <see cref="LastData"/>
    /// for the value carried across those states. For an infinite query it is an array of the loaded pages
    /// (<c>TData[]</c>), retained across fetches and failures, or <c>null</c> when no page has loaded.
    /// </summary>
    /// <remarks>
    /// The value is a consumer type. Rendering or serializing it is the inspector's job; note that
    /// reflection-based serialization is not trim- or AOT-safe.
    /// </remarks>
    public object? CurrentData { get; }

    /// <summary>
    /// For a regular query, the data from the previous successful fetch, carried forward across subsequent
    /// fetches and failures — the value to show while <see cref="CurrentData"/> is <c>null</c> during a
    /// refetch. Always <c>null</c> for an infinite query, whose <see cref="CurrentData"/> already retains
    /// its pages across fetches.
    /// </summary>
    public object? LastData { get; }

    /// <summary>The exception from the most recent failed fetch; <c>null</c> unless <see cref="Status"/> is <see cref="QueryStatus.Failure"/>.</summary>
    public Exception? Error { get; }

    /// <summary>
    /// When data was last written to the entry — by a successful fetch or a <c>SetData</c> call — on the
    /// client's scheduler clock. <c>null</c> until the first success.
    /// </summary>
    public DateTimeOffset? LastUpdatedAt { get; }

    /// <summary>The number of active subscribers to the entry's state.</summary>
    public int ObserverCount { get; }

    /// <summary>
    /// <c>true</c> when the entry would be re-fetched by an invalidation or by its next subscriber: it has
    /// never succeeded, its data is older than its <c>StaleTime</c>, or it was invalidated while it had no
    /// subscribers.
    /// </summary>
    /// <remarks>
    /// Evaluated when read. An entry becoming stale purely through elapsed time does not cause an
    /// <see cref="IQueryCacheInspector.Entries"/> emission.
    /// </remarks>
    public bool IsStale { get; }

    /// <summary>How long the entry stays in the cache after its last subscriber disposes.</summary>
    public TimeSpan CacheTime { get; }

    /// <summary>Triggers an immediate re-fetch of the entry, bypassing stale-time checks.</summary>
    public void Refetch();

    /// <summary>
    /// Marks the entry as stale. Re-fetches immediately when it has subscribers; otherwise the fetch is
    /// deferred until the next subscriber joins. A no-op while the data is still within its <c>StaleTime</c>
    /// window — use <see cref="Refetch"/> to force a fetch.
    /// </summary>
    public void Invalidate();

    /// <summary>Cancels the entry's in-flight fetch, if any.</summary>
    public void Cancel();
}

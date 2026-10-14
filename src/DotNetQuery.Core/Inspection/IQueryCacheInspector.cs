namespace DotNetQuery.Core.Inspection;

/// <summary>
/// Read access to the live contents of a query client's cache, for building developer tools such as a
/// cache inspector. Obtain one with <see cref="QueryClientInspectionExtensions.TryGetInspector"/>.
/// </summary>
public interface IQueryCacheInspector
{
    /// <summary>
    /// Emits the cache's entries — the current list immediately on subscribe, then a fresh list whenever an
    /// entry is added, evicted, has a pending eviction cancelled, or changes state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each emitted list is a snapshot of which entries are in the cache; the entries themselves are live
    /// views (see <see cref="IQueryCacheEntry"/>). Emissions are not coalesced, so a fetch produces several
    /// in quick succession. The sequence completes when the client is disposed.
    /// </para>
    /// <para>
    /// <b>Threading:</b> emissions arrive on whichever thread changed the cache — typically a thread-pool
    /// thread finishing a fetch, or the scheduler's timer thread for evictions. UI consumers must marshal
    /// onto the UI thread before touching UI state: in Blazor, <c>InvokeAsync</c>; with
    /// <c>DotNetQuery.Mvvm</c>, <c>ObserveOnUi()</c>.
    /// </para>
    /// </remarks>
    public IObservable<IReadOnlyList<IQueryCacheEntry>> Entries { get; }
}

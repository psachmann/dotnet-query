namespace Fetchling.Core.Internals;

/// <summary>
/// A cache entry whose data can be written by <see cref="IQueryClient"/> without knowing the entry's args
/// type. Implemented by <see cref="Query{TArgs,TData}"/>; an infinite query holds pages, not a single
/// <typeparamref name="TData"/>, and deliberately does not implement it.
/// </summary>
internal interface IWritableEntry<TData>
    where TData : class
{
    /// <summary>
    /// Writes the value <paramref name="write"/> returns for the entry's current data (<c>null</c> when it has
    /// none) as a fresh success state, superseding a fetch in flight. Returns <c>false</c> without writing when
    /// <paramref name="write"/> returns <c>null</c>, a stream is running, or the entry is disposed.
    /// </summary>
    public bool TryWrite(Func<TData?, TData?> write);
}

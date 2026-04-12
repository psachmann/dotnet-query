namespace DotNetQuery.Core;

/// <summary>
/// The central client for creating and managing queries and mutations.
/// Acts as the cache owner — all queries and mutations created through it share the same cache.
/// </summary>
public interface IQueryClient : IDisposable
{
    /// <summary>
    /// Gets or creates a query that accepts parameters for the given key.
    /// If a query with the same key already exists in the cache, it is returned as-is.
    /// </summary>
    IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options);

    /// <summary>
    /// Marks all queries matching the given key as stale and triggers a re-fetch.
    /// </summary>
    void Invalidate(QueryKey key);

    /// <summary>
    /// Marks all queries whose key matches the predicate as stale and triggers a re-fetch.
    /// </summary>
    void Invalidate(Func<QueryKey, bool> predicate);

    /// <summary>
    /// Creates a mutation. If <see cref="MutationOptions{TArgs, TData}.InvalidateKeys"/> is set,
    /// the specified keys are invalidated automatically on success.
    /// </summary>
    IMutation<TArgs, TData> CreateMutation<TArgs, TData>(MutationOptions<TArgs, TData> options);

    /// <summary>
    /// Writes <paramref name="data"/> directly into the cache for the given key, bypassing a fetch.
    /// Subscribers to the affected query will receive an immediate <c>Success</c> state update.
    /// Has no effect if the key has no active cache entry.
    /// </summary>
    void SetQueryData<TData>(QueryKey key, TData data);

    /// <summary>
    /// Returns the current cached data for <paramref name="key"/>, or <c>default</c>
    /// if the key has no active cache entry or the cached type does not match <typeparamref name="TData"/>.
    /// </summary>
    TData? GetQueryData<TData>(QueryKey key);
}

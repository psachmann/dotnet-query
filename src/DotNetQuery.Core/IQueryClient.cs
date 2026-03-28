namespace DotNetQuery.Core;

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
}

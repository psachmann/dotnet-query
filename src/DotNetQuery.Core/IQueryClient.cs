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
    IQuery<TArgs, TData> CreateQuery<TArgs, TData>(QueryOptions<TArgs, TData> options)
        where TData : class;

    /// <summary>
    /// Gets or creates an infinite (paginated) query. Queries with the same key share one cache entry.
    /// Use <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchNextPage"/> and
    /// <see cref="IInfiniteQuery{TArgs,TData,TPageParam}.FetchPreviousPage"/> to accumulate pages.
    /// </summary>
    IInfiniteQuery<TArgs, TData, TPageParam> CreateInfiniteQuery<TArgs, TData, TPageParam>(
        InfiniteQueryOptions<TArgs, TData, TPageParam> options
    )
        where TData : class;

    /// <summary>
    /// Creates a mutation. If <see cref="MutationOptions{TArgs, TData}.InvalidateKeys"/> is set,
    /// the specified keys are invalidated automatically on success.
    /// </summary>
    IMutation<TArgs, TData> CreateMutation<TArgs, TData>(MutationOptions<TArgs, TData> options);

    /// <summary>
    /// Creates a mutation whose mutator reports progress through an <see cref="IProgress{T}"/>; the latest
    /// value travels on <see cref="IMutation{TArgs,TData,TProgress}.State"/> as
    /// <see cref="MutationState{TData,TProgress}.Progress"/>. If
    /// <see cref="MutationOptions{TArgs,TData,TProgress}.InvalidateKeys"/> is set, the specified keys are
    /// invalidated automatically on success.
    /// </summary>
    IMutation<TArgs, TData, TProgress> CreateMutation<TArgs, TData, TProgress>(
        MutationOptions<TArgs, TData, TProgress> options
    );

    /// <summary>
    /// Marks all queries matching the given key as stale and, for entries with an active
    /// subscriber, triggers a re-fetch. Entries still within their <c>StaleTime</c> window are
    /// left untouched, and entries with no active subscriber are only marked stale — the re-fetch
    /// is deferred until the next subscriber arrives.
    /// </summary>
    void Invalidate(QueryKey key);

    /// <summary>
    /// Marks all queries whose key matches the predicate as stale and, for entries with an active
    /// subscriber, triggers a re-fetch. Entries still within their <c>StaleTime</c> window are
    /// left untouched, and entries with no active subscriber are only marked stale — the re-fetch
    /// is deferred until the next subscriber arrives.
    /// </summary>
    void Invalidate(Func<QueryKey, bool> predicate);
}

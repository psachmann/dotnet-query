namespace Fetchling.Core;

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
    /// Gets or creates a streamed query: its fetcher yields chunks, which are folded into the data as they
    /// arrive, so the data can be rendered while it grows. Queries with the same key share one cache entry
    /// and one running stream. The result is a regular <see cref="IQuery{TArgs,TData}"/>; see
    /// <see cref="StreamedQueryOptions{TArgs,TData,TChunk}"/> for how streaming shows up in its state.
    /// </summary>
    IQuery<TArgs, TData> CreateStreamedQuery<TArgs, TData, TChunk>(StreamedQueryOptions<TArgs, TData, TChunk> options)
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
    /// Marks the query with exactly the given key as stale and, if it has an active
    /// subscriber, triggers a re-fetch. The key must match in full; it is not a prefix — use
    /// <see cref="Invalidate(Func{QueryKey, bool})"/> to invalidate a group of keys. Entries still within their <c>StaleTime</c> window are
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

    /// <summary>
    /// Writes <paramref name="data"/> into the cached query with exactly the given key, as a success state —
    /// for example data pushed by a server over SignalR or server-sent events. The entry is stamped fresh, and
    /// a fetch in flight for it is cancelled, so its older result can't overwrite the written data.
    /// Safe to call from any thread.
    /// </summary>
    /// <remarks>
    /// Only an existing entry is written: there is no fetcher to create one with, and no entry means no observer
    /// is interested. A streamed query is written only while no stream is running, because the next chunk would
    /// overwrite the data.
    /// </remarks>
    /// <returns>
    /// <c>true</c> when the data was written; <c>false</c> when the key isn't cached or a stream is running.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The key is held by a query whose data type isn't <typeparamref name="TData"/>, or by an infinite query.
    /// </exception>
    bool SetData<TData>(QueryKey key, TData data)
        where TData : class;

    /// <summary>
    /// Replaces the data of the cached query with exactly the given key with what <paramref name="updater"/>
    /// returns for its current data — for example, swapping one pushed item into a cached list. The updater is
    /// called only when the entry has data (the latest, or the last shown during a refetch or after a failure).
    /// Otherwise behaves like <see cref="SetData{TData}(QueryKey, TData)"/>.
    /// </summary>
    /// <param name="key">The key of the query to update.</param>
    /// <param name="updater">
    /// Returns the new data for the current data. Must not mutate its input: the current data is an immutable
    /// snapshot that earlier states still hold.
    /// </param>
    /// <returns>
    /// <c>true</c> when the data was written; <c>false</c> when the key isn't cached, the entry has no data,
    /// or a stream is running.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The key is held by a query whose data type isn't <typeparamref name="TData"/>, or by an infinite query.
    /// </exception>
    bool SetData<TData>(QueryKey key, Func<TData, TData> updater)
        where TData : class;

    /// <summary>
    /// Applies <paramref name="updater"/> to the data of every cached query whose key matches
    /// <paramref name="predicate"/> and whose data type is <typeparamref name="TData"/> — for example, patching
    /// one pushed item into every cached list that may contain it. Entries of another data type, infinite
    /// queries, entries without data and running streams are skipped, so a broad predicate is safe. Each
    /// written entry behaves as with <see cref="SetData{TData}(QueryKey, Func{TData, TData})"/>.
    /// </summary>
    /// <returns>The number of entries written.</returns>
    int SetData<TData>(Func<QueryKey, bool> predicate, Func<TData, TData> updater)
        where TData : class;
}

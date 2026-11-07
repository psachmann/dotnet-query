namespace DotNetQuery.Core;

/// <summary>
/// Configuration for a streamed query created via
/// <see cref="IQueryClient.CreateStreamedQuery{TArgs,TData,TChunk}"/>: the fetcher yields chunks, and
/// <see cref="Reducer"/> folds each one into the data, starting from <see cref="InitialValue"/>.
/// Use it for responses that arrive in pieces and should be shown as they grow — server-streaming gRPC
/// calls, LLM token output, and the like.
/// </summary>
/// <remarks>
/// <para>
/// The query is <see cref="QueryStatus.Fetching"/> until the first chunk arrives, then
/// <see cref="QueryStatus.Success"/> with <see cref="QueryState{TData}.IsStreaming"/> set while more chunks
/// come in. <see cref="IQuery{TArgs,TData}.State"/> emits once per chunk with the data folded so far;
/// <see cref="IQuery{TArgs,TData}.Success"/> and <see cref="IQuery{TArgs,TData}.Settled"/> emit once, when
/// the stream completes.
/// </para>
/// <para>
/// Caching, deduplication, stale time, cancellation and retries work exactly as for a regular query, applied
/// to the folded data. A retry restarts the stream from <see cref="InitialValue"/>. Unlike a regular query,
/// <see cref="IQuery.Invalidate"/> does not restart a stream that is still running — it only marks the
/// data stale; <see cref="IQuery.Refetch"/> does restart it.
/// </para>
/// </remarks>
/// <typeparam name="TArgs">The type of the arguments that identify which stream to fetch.</typeparam>
/// <typeparam name="TData">
/// The type of the folded data. Constrained to reference types because the library uses <c>null</c> to
/// represent "no data yet".
/// </typeparam>
/// <typeparam name="TChunk">The type of a single chunk the stream yields.</typeparam>
public sealed record StreamedQueryOptions<TArgs, TData, TChunk>
    where TData : class
{
    /// <summary>Derives the cache key from the args. Queries with the same key share one running stream.</summary>
    public required Func<TArgs, QueryKey> KeyFactory { get; init; }

    /// <summary>
    /// Opens the stream for the given args. Receives a <see cref="CancellationToken"/> that is cancelled when
    /// the query is cancelled, disposed, or superseded by a refetch; enumeration stops as soon as it is.
    /// Called again from the start on every refetch and retry.
    /// </summary>
    public required Func<TArgs, CancellationToken, IAsyncEnumerable<TChunk>> Fetcher { get; init; }

    /// <summary>
    /// The value every fetch starts folding from — e.g. <c>""</c> for text, or an empty immutable list.
    /// This is the seed of the fold, not pre-populated cache data: a stream that yields no chunks completes
    /// with this value as its data.
    /// </summary>
    public required TData InitialValue { get; init; }

    /// <summary>
    /// Folds one chunk into the data so far and returns the new data, e.g.
    /// <c>(text, token) =&gt; text + token</c>. Must not mutate its input: every intermediate value is
    /// published as a state, and a state is an immutable snapshot.
    /// </summary>
    public required Func<TData, TChunk, TData> Reducer { get; init; }

    /// <summary>
    /// What to show while refetching data the query already has. Defaults to <see cref="StreamRefetchMode.Reset"/>.
    /// The very first fetch always shows chunks as they arrive, since there is nothing to keep.
    /// </summary>
    public StreamRefetchMode RefetchMode { get; init; } = StreamRefetchMode.Reset;

    /// <summary>Overrides the global <see cref="QueryClientOptions.StaleTime"/>. <c>null</c> uses the global default.</summary>
    public TimeSpan? StaleTime { get; init; }

    /// <summary>Overrides the global <see cref="QueryClientOptions.CacheTime"/>. <c>null</c> uses the global default.</summary>
    public TimeSpan? CacheTime { get; init; }

    /// <summary>
    /// Refetches on this interval. Unlike other query options, <c>null</c> does <b>not</b> fall back to
    /// <see cref="QueryClientOptions.RefetchInterval"/>: re-opening a stream is rarely cheap (think of an LLM
    /// call), so a streamed query only polls when it asks to.
    /// </summary>
    public TimeSpan? RefetchInterval { get; init; }

    /// <summary>
    /// Overrides the global <see cref="QueryClientOptions.RetryHandler"/>. <c>null</c> uses the global default.
    /// Each attempt restarts the stream from <see cref="InitialValue"/>.
    /// </summary>
    public IRetryHandler? RetryHandler { get; init; }

    /// <summary>
    /// A short, low-cardinality name used to tag metrics for this query. When <c>null</c>, the first part of
    /// the derived <see cref="QueryKey"/> is used instead. See <see cref="QueryOptions{TArgs,TData}.Name"/>.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Whether the query is initially enabled. Defaults to <c>true</c>.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Compares the completed data against the previous completed data. When equal, the previous reference is
    /// kept and <see cref="IQuery{TArgs,TData}.Success"/> does not re-emit. Not applied to the intermediate
    /// values published while streaming. Defaults to <see cref="EqualityComparer{T}.Default"/>.
    /// </summary>
    public IEqualityComparer<TData>? DataComparer { get; init; }
}

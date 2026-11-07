namespace DotNetQuery.Core.Internals;

/// <summary>
/// Turns a <see cref="Query{TArgs,TData}"/> into a streamed one. Built from
/// <see cref="StreamedQueryOptions{TArgs,TData,TChunk}"/>, with the chunk type folded away so the query itself
/// stays generic over <typeparamref name="TArgs"/> and <typeparamref name="TData"/> only.
/// </summary>
internal sealed record StreamedFetch<TArgs, TData>
    where TData : class
{
    /// <summary>
    /// Opens the stream for the args and folds every chunk, invoking the callback with the folded value after
    /// each one; completes with the final value (the seed, if the stream yields nothing). The callback may
    /// throw to stop enumeration.
    /// </summary>
    public required Func<TArgs, Action<TData>, CancellationToken, Task<TData>> FetchAsync { get; init; }

    public required StreamRefetchMode RefetchMode { get; init; }

    public static StreamedFetch<TArgs, TData> Create<TChunk>(StreamedQueryOptions<TArgs, TData, TChunk> options)
    {
        var fetcher = options.Fetcher;
        var reducer = options.Reducer;
        var seed = options.InitialValue;

        return new()
        {
            FetchAsync = async (args, onChunk, ct) =>
            {
                var data = seed;

                await foreach (var chunk in fetcher(args, ct).WithCancellation(ct).ConfigureAwait(false))
                {
                    data = reducer(data, chunk);
                    onChunk(data);
                }

                return data;
            },
            RefetchMode = options.RefetchMode,
        };
    }
}

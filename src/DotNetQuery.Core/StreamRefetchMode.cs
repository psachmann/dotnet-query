namespace DotNetQuery.Core;

/// <summary>
/// What a streamed query shows while it refetches data it already has.
/// See <see cref="StreamedQueryOptions{TArgs,TData,TChunk}.RefetchMode"/>.
/// </summary>
public enum StreamRefetchMode : byte
{
    /// <summary>
    /// Drop the previous data as soon as the refetch starts and show the new stream as it grows —
    /// what a chat-style UI expects. This is the default.
    /// </summary>
    Reset = 0,

    /// <summary>
    /// Keep showing the previous data (as <see cref="QueryState{TData}.LastData"/>, like a regular
    /// query's background refetch) and swap in the new data once the stream completes. Chunks are
    /// folded out of sight in the meantime.
    /// </summary>
    Replace = 1,
}

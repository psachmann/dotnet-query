namespace DotNetQuery.Core;

/// <summary>
/// Contextual information passed to <c>GetNextPageParam</c> and <c>GetPreviousPageParam</c>
/// to derive the cursor or offset for the next fetch.
/// </summary>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
/// <param name="Page">
/// The boundary page — the <em>last</em> page for <c>GetNextPageParam</c>,
/// the <em>first</em> page for <c>GetPreviousPageParam</c>.
/// </param>
/// <param name="AllPages">All currently loaded pages, in order from oldest to newest.</param>
/// <param name="PageParam">The parameter that was used to fetch <paramref name="Page"/>.</param>
/// <param name="AllPageParams">
/// The parameters used to fetch each page in <paramref name="AllPages"/>.
/// <c>AllPageParams[i]</c> corresponds to <c>AllPages[i]</c>.
/// </param>
public readonly record struct InfinitePageInfo<TData, TPageParam>(
    TData Page,
    IReadOnlyList<TData> AllPages,
    TPageParam PageParam,
    IReadOnlyList<TPageParam> AllPageParams
);

namespace DotNetQuery.Blazor;

/// <summary>
/// Renders infinite query state with explicit loading and failure templates.
/// Shows <c>Loading</c> while the query is <c>Idle</c> or <c>Fetching</c> (including full
/// background refetches), <c>Content</c> once data has been successfully loaded, and
/// <c>Failure</c> whenever the query has failed — even when stale pages from an earlier
/// success are still available.
/// </summary>
/// <remarks>
/// Use <see cref="InfiniteSuspense{TArgs,TData,TPageParam}"/> when you want a clean loading
/// state on every full refetch, even if stale pages are available.
/// For stale-while-revalidate semantics — keeping existing pages visible during background
/// refetches and next-page fetches — use <see cref="InfiniteTransition{TArgs,TData,TPageParam}"/> instead.
/// </remarks>
/// <typeparam name="TArgs">The type of arguments that identify the resource to fetch.</typeparam>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor / offset).</typeparam>
public partial class InfiniteSuspense<TArgs, TData, TPageParam>;

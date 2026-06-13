namespace DotNetQuery.Blazor;

/// <summary>
/// Renders infinite query state with stale-while-revalidate semantics.
/// Shows <c>Content</c> whenever at least one page is available — including during background
/// full refetches and while additional pages are loading — so existing results stay visible
/// without flicker. Shows <c>Loading</c> only on the very first fetch before any data arrives,
/// and <c>Failure</c> only when the query fails with no pages to fall back on.
/// </summary>
/// <remarks>
/// Use <see cref="InfiniteTransition{TArgs,TData,TPageParam}"/> for lists that update frequently
/// or where the "Load more" interaction should not hide previously loaded pages.
/// For a strict loading state on every full refetch, use
/// <see cref="InfiniteSuspense{TArgs,TData,TPageParam}"/> instead.
/// </remarks>
/// <typeparam name="TArgs">The type of arguments that identify the resource to fetch.</typeparam>
/// <typeparam name="TData">The type of a single page of data.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor / offset).</typeparam>
public partial class InfiniteTransition<TArgs, TData, TPageParam>;

namespace DotNetQuery.Blazor;

/// <summary>
/// Renders query state with explicit loading and failure templates.
/// Shows <c>Loading</c> while the query is <c>Idle</c> or <c>Fetching</c> (including background
/// re-fetches), <c>Content</c> on success, and <c>Failure</c> on error.
/// </summary>
/// <remarks>
/// Use <see cref="Suspense{TArgs,TData}"/> when you want a clean loading state between navigations or
/// when showing stale data during a background refetch would be misleading.
/// For stale-while-revalidate semantics, use <see cref="Transition{TArgs,TData}"/> instead.
/// </remarks>
/// <typeparam name="TArgs">The type of arguments passed to the query fetcher.</typeparam>
/// <typeparam name="TData">The type of data returned by the query fetcher.</typeparam>
public partial class Suspense<TArgs, TData>;

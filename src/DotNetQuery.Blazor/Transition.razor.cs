namespace DotNetQuery.Blazor;

/// <summary>
/// Renders query state with stale-while-revalidate semantics.
/// Keeps showing the previous <c>Content</c> during background re-fetches instead of switching
/// to a loading indicator. Only falls back to <c>Loading</c> when no data has ever been fetched.
/// </summary>
/// <remarks>
/// Use <see cref="Transition{TArgs,TData}"/> when the data updates frequently and switching to a
/// loading spinner on every refetch would feel jarring — for example, a periodically refreshed list.
/// For an explicit loading state on every fetch, use <see cref="Suspense{TArgs,TData}"/> instead.
/// </remarks>
/// <typeparam name="TArgs">The type of arguments passed to the query fetcher.</typeparam>
/// <typeparam name="TData">The type of data returned by the query fetcher.</typeparam>
public partial class Transition<TArgs, TData>;

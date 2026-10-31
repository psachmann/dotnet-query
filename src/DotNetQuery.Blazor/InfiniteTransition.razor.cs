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
public partial class InfiniteTransition<TArgs, TData, TPageParam> : IDisposable
{
    private InfiniteQueryState<TData, TPageParam> _state = InfiniteQueryState<TData, TPageParam>.CreateIdle();
    private IDisposable _subscription = Disposable.Empty;
    private IInfiniteQuery<TArgs, TData, TPageParam>? _subscribedQuery;

    /// <summary>
    /// The infinite query whose state drives rendering. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public IInfiniteQuery<TArgs, TData, TPageParam> Query { get; set; } = default!;

    /// <summary>
    /// Rendered whenever at least one page is available — including during background full refetches
    /// and while <see cref="InfiniteQueryState{TData,TPageParam}.IsFetchingNextPage"/> is true.
    /// Receives the full query state as context. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public RenderFragment<InfiniteQueryState<TData, TPageParam>> Content { get; set; } = default!;

    /// <summary>
    /// Rendered only before any page has been loaded (initial fetch with no cached data).
    /// Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment? Loading { get; set; }

    /// <summary>
    /// Rendered when the query has failed and no pages are available to fall back on.
    /// Receives the exception as context. Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment<Exception>? Failure { get; set; }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(Query, _subscribedQuery))
        {
            return;
        }

        _subscription.Dispose();
        _subscription = Disposable.Empty;
        _subscribedQuery = null;

        _subscription = Query.State.Subscribe(state =>
        {
            _state = state;
            _ = InvokeAsync(StateHasChanged);
        });

        _subscribedQuery = Query;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _subscription.Dispose();
        GC.SuppressFinalize(this);
    }
}

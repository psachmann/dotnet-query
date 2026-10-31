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
public partial class InfiniteSuspense<TArgs, TData, TPageParam> : IDisposable
{
    private InfiniteQueryState<TData, TPageParam> _state = InfiniteQueryState<TData, TPageParam>.CreateIdle();
    private IDisposable? _subscription;
    private IInfiniteQuery<TArgs, TData, TPageParam>? _subscribedQuery;

    /// <summary>
    /// The infinite query whose state drives rendering. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public IInfiniteQuery<TArgs, TData, TPageParam> Query { get; set; } = default!;

    /// <summary>
    /// Rendered when data has been successfully loaded. Receives the full query state as context,
    /// including all accumulated pages, <see cref="InfiniteQueryState{TData,TPageParam}.HasNextPage"/>,
    /// and <see cref="InfiniteQueryState{TData,TPageParam}.IsFetchingNextPage"/>. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public RenderFragment<InfiniteQueryState<TData, TPageParam>> Content { get; set; } = default!;

    /// <summary>
    /// Rendered while the query is <c>Idle</c> or <c>Fetching</c>, including during background
    /// full refetches even when stale pages are available.
    /// Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment? Loading { get; set; }

    /// <summary>
    /// Rendered whenever the query has failed, even when stale pages are still available —
    /// use <see cref="InfiniteTransition{TArgs,TData,TPageParam}"/> to keep showing stale pages instead.
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

        _subscribedQuery = Query;
        _subscription?.Dispose();
        _subscription = Query.State.Subscribe(state =>
        {
            _state = state;
            _ = InvokeAsync(StateHasChanged);
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _subscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}

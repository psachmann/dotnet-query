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
/// <typeparam name="TData">The type of data returned by the query fetcher. Constrained to reference types.</typeparam>
public partial class Transition<TArgs, TData> : IDisposable
    where TData : class
{
    private QueryState<TData> _state = QueryState<TData>.CreateIdle();
    private TData? _displayData => _state.CurrentData ?? _state.LastData;
    private IDisposable _subscription = Disposable.Empty;
    private IQuery<TArgs, TData>? _subscribedQuery;

    /// <summary>
    /// The query whose state drives rendering. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public IQuery<TArgs, TData> Query { get; set; } = default!;

    /// <summary>
    /// Rendered when data is available — either freshly fetched or stale data from a previous fetch.
    /// Receives the data as context. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public RenderFragment<TData> Content { get; set; } = default!;

    /// <summary>
    /// Rendered only when no data is available at all (initial load before the first successful fetch).
    /// During background refetches, <see cref="Content"/> continues to show the previous data instead.
    /// Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment? Loading { get; set; }

    /// <summary>
    /// Rendered when the query has failed and no previous data is available to fall back on.
    /// Receives the exception as context. Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment<Exception>? Failure { get; set; }

    ///<inheritdoc/>
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

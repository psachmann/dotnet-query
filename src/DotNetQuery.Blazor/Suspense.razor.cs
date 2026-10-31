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
/// <typeparam name="TData">The type of data returned by the query fetcher. Constrained to reference types.</typeparam>
public partial class Suspense<TArgs, TData> : IDisposable
    where TData : class
{
    private QueryState<TData> _state = QueryState<TData>.CreateIdle();
    private IDisposable? _subscription;
    private IQuery<TArgs, TData>? _subscribedQuery;

    /// <summary>
    /// The query whose state drives rendering. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public IQuery<TArgs, TData> Query { get; set; } = default!;

    /// <summary>
    /// Rendered when the query succeeds. Receives the fetched data as context. Required.
    /// </summary>
    [Parameter, EditorRequired]
    public RenderFragment<TData> Content { get; set; } = default!;

    /// <summary>
    /// Rendered while the query is <c>Idle</c> or <c>Fetching</c>, including during background re-fetches.
    /// Defaults to nothing if not provided.
    /// </summary>
    [Parameter]
    public RenderFragment? Loading { get; set; }

    /// <summary>
    /// Rendered when the query has failed. Receives the exception as context.
    /// Defaults to nothing if not provided.
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

namespace DotNetQuery.Mvvm;

/// <summary>
/// An <see cref="INotifyPropertyChanged"/> view model wrapping an
/// <see cref="IInfiniteQuery{TArgs, TData, TPageParam}"/> for MVVM-based UI frameworks
/// (MAUI, WPF, WinUI, UNO, Avalonia). Exposes the paginated query state as bindable properties and
/// marshals all change notifications onto the UI thread via an <see cref="IUiDispatcher"/>.
/// <para>
/// The view model holds a live subscription to <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.State"/>
/// for its entire lifetime — this is what keeps the underlying cache entry retained and triggers
/// deferred stale fetches. Dispose the view model when its page is torn down; a forgotten dispose keeps
/// the cache entry alive indefinitely.
/// </para>
/// <para>
/// Pages are not flattened: <typeparamref name="TData"/> is a single page and is not necessarily
/// enumerable. Flatten in the owning page view model, e.g. by syncing an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> from <see cref="Pages"/> off
/// <see cref="StateChanged"/>.
/// </para>
/// </summary>
/// <typeparam name="TArgs">The type of the parameters passed to the fetcher.</typeparam>
/// <typeparam name="TData">The type of a single page of data. Constrained to reference types.</typeparam>
/// <typeparam name="TPageParam">The type of the page parameter (cursor/offset).</typeparam>
public class InfiniteQueryViewModel<TArgs, TData, TPageParam> : BindableBase, IDisposable
    where TData : class
{
    private readonly bool _ownsQuery;
    private readonly RelayCommand _refetchCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly RelayCommand _fetchNextPageCommand;
    private readonly RelayCommand _fetchPreviousPageCommand;
    private readonly UiStateBinding<InfiniteQueryState<TData, TPageParam>> _stateBinding;

    private bool _isDisposed;

    /// <summary>
    /// Wraps an existing infinite query. The view model does <b>not</b> take ownership:
    /// <see cref="Dispose()"/> releases only the view model's own state subscription,
    /// and the caller remains responsible for disposing <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The infinite query to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public InfiniteQueryViewModel(IInfiniteQuery<TArgs, TData, TPageParam> query, IUiDispatcher? dispatcher = null)
        : this(query, ownsQuery: false, dispatcher) { }

    /// <summary>
    /// Creates a new infinite query observer via
    /// <see cref="IQueryClient.CreateInfiniteQuery{TArgs, TData, TPageParam}"/> and wraps it. The view
    /// model owns the created query and disposes it in <see cref="Dispose()"/>. This is the recommended
    /// path for page view models that receive an <see cref="IQueryClient"/> through dependency injection.
    /// </summary>
    /// <param name="client">The query client used to create the observer.</param>
    /// <param name="options">The infinite query options.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public InfiniteQueryViewModel(
        IQueryClient client,
        InfiniteQueryOptions<TArgs, TData, TPageParam> options,
        IUiDispatcher? dispatcher = null
    )
        : this(CreateQuery(client, options), ownsQuery: true, dispatcher) { }

    private InfiniteQueryViewModel(
        IInfiniteQuery<TArgs, TData, TPageParam> query,
        bool ownsQuery,
        IUiDispatcher? dispatcher
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
        _ownsQuery = ownsQuery;

        // See QueryViewModel for why these are constructed before _stateBinding: the null-forgiving
        // operator overrides the compiler's constructor-local flow analysis, not an actual hazard —
        // RelayCommand never invokes canExecute from its own constructor.
        _refetchCommand = new RelayCommand(query.Refetch, () => !IsAnyFetchInFlight(_stateBinding!.Current));
        _cancelCommand = new RelayCommand(query.Cancel, () => IsAnyFetchInFlight(_stateBinding!.Current));
        _fetchNextPageCommand = new RelayCommand(
            query.FetchNextPage,
            () => _stateBinding!.Current.HasNextPage && !IsAnyFetchInFlight(_stateBinding.Current)
        );
        _fetchPreviousPageCommand = new RelayCommand(
            query.FetchPreviousPage,
            () => _stateBinding!.Current.HasPreviousPage && !IsAnyFetchInFlight(_stateBinding.Current)
        );

        // Apply the current state synchronously so bindings evaluated right after construction
        // read correct values without waiting for a dispatcher hop. See UiStateBinding for how the
        // replay delivered by the subsequent Subscribe is deduped.
        _stateBinding = new UiStateBinding<InfiniteQueryState<TData, TPageParam>>(
            query.State,
            query.CurrentState,
            dispatcher ?? SynchronizationContextUiDispatcher.CaptureCurrent(),
            ApplyState
        );
    }

    /// <summary>
    /// The wrapped query — the escape hatch for members without a bindable counterpart
    /// (<see cref="IQuery.Invalidate"/>, <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.Detach"/>,
    /// or direct Rx composition via <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.Success"/>,
    /// <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.Failure"/>,
    /// <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.Settled"/>).
    /// </summary>
    public IInfiniteQuery<TArgs, TData, TPageParam> Query { get; }

    /// <summary>The raw state snapshot, for bindings and converters that need the whole state.</summary>
    public InfiniteQueryState<TData, TPageParam> CurrentState => _stateBinding.Current;

    /// <summary>The current lifecycle status of the query.</summary>
    public QueryStatus Status => _stateBinding.Current.Status;

    /// <summary>All currently loaded pages, ordered from oldest to newest.</summary>
    public IReadOnlyList<TData> Pages => _stateBinding.Current.Pages;

    /// <summary>
    /// The page parameters used to fetch each page in <see cref="Pages"/>.
    /// <c>PageParams[i]</c> was the parameter used to fetch <c>Pages[i]</c>.
    /// </summary>
    public IReadOnlyList<TPageParam> PageParams => _stateBinding.Current.PageParams;

    /// <summary>The exception from the most recent failed fetch. <c>null</c> when not in a failure state.</summary>
    public Exception? Error => _stateBinding.Current.Error;

    /// <summary><c>true</c> when the query is idle.</summary>
    public bool IsIdle => _stateBinding.Current.IsIdle;

    /// <summary>
    /// <c>true</c> while a full refetch is in flight. See <see cref="IsFetchingNextPage"/> and
    /// <see cref="IsFetchingPreviousPage"/> for pagination fetches, which do not set this.
    /// </summary>
    public bool IsFetching => _stateBinding.Current.IsFetching;

    /// <summary><c>true</c> when the most recent fetch succeeded.</summary>
    public bool IsSuccess => _stateBinding.Current.IsSuccess;

    /// <summary><c>true</c> when the most recent fetch failed.</summary>
    public bool IsFailure => _stateBinding.Current.IsFailure;

    /// <summary><c>true</c> when at least one page has been successfully loaded.</summary>
    public bool HasData => _stateBinding.Current.HasData;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => _stateBinding.Current.HasError;

    /// <summary><c>true</c> when a next page is available to fetch.</summary>
    public bool HasNextPage => _stateBinding.Current.HasNextPage;

    /// <summary><c>true</c> when a previous page is available to fetch.</summary>
    public bool HasPreviousPage => _stateBinding.Current.HasPreviousPage;

    /// <summary><c>true</c> while a next-page fetch is in progress. Existing pages remain visible.</summary>
    public bool IsFetchingNextPage => _stateBinding.Current.IsFetchingNextPage;

    /// <summary><c>true</c> while a previous-page fetch is in progress. Existing pages remain visible.</summary>
    public bool IsFetchingPreviousPage => _stateBinding.Current.IsFetchingPreviousPage;

    /// <summary>
    /// <c>true</c> only during the first load — fetching with no pages loaded yet.
    /// Bind a full-page loading indicator to this and a subtle refresh indicator to
    /// <see cref="IsFetching"/>.
    /// </summary>
    public bool IsLoading => ComputeIsLoading(_stateBinding.Current);

    /// <summary>
    /// Triggers <see cref="IQuery.Refetch"/> for the whole page set. Disabled while any fetch is in flight.
    /// </summary>
    public ICommand RefetchCommand => _refetchCommand;

    /// <summary>
    /// Triggers <see cref="IQuery.Cancel"/>. Enabled while any fetch is in flight.
    /// </summary>
    public ICommand CancelCommand => _cancelCommand;

    /// <summary>
    /// Triggers <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.FetchNextPage"/>. Enabled only
    /// when <see cref="HasNextPage"/> is <c>true</c> and no fetch is currently in flight.
    /// </summary>
    public ICommand FetchNextPageCommand => _fetchNextPageCommand;

    /// <summary>
    /// Triggers <see cref="IInfiniteQuery{TArgs, TData, TPageParam}.FetchPreviousPage"/>. Enabled only
    /// when <see cref="HasPreviousPage"/> is <c>true</c> and no fetch is currently in flight.
    /// </summary>
    public ICommand FetchPreviousPageCommand => _fetchPreviousPageCommand;

    /// <summary>
    /// Raised on the UI thread after every <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// notification for an applied state. Use this — instead of a second raw subscription to
    /// <see cref="Query"/>'s state — for page-specific work such as syncing an
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/> from <see cref="Pages"/>.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <inheritdoc cref="IInfiniteQuery{TArgs, TData, TPageParam}.SetArgs" />
    public void SetArgs(TArgs args) => Query.SetArgs(args);

    /// <inheritdoc cref="IInfiniteQuery{TArgs, TData, TPageParam}.SetEnabled" />
    public void SetEnabled(bool enabled) => Query.SetEnabled(enabled);

    /// <summary>
    /// Releases the view model's state subscription — which lets the cache entry's eviction clock
    /// start — and disposes the wrapped query if this view model created it.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources. Override in derived view models to dispose additional state;
    /// always call the base implementation.
    /// </summary>
    /// <param name="disposing"><c>true</c> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (disposing)
        {
            _stateBinding.Dispose();

            if (_ownsQuery)
            {
                Query.Dispose();
            }
        }
    }

    private static IInfiniteQuery<TArgs, TData, TPageParam> CreateQuery(
        IQueryClient client,
        InfiniteQueryOptions<TArgs, TData, TPageParam> options
    )
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        return client.CreateInfiniteQuery(options);
    }

    // CreateFetchingNext/Previous report Status == Success (existing pages stay visible while the
    // extra page loads), so IsFetching alone misses them — every "is a fetch running" check needs
    // this instead.
    private static bool IsAnyFetchInFlight(InfiniteQueryState<TData, TPageParam> state) =>
        state.IsFetching || state.IsFetchingNextPage || state.IsFetchingPreviousPage;

    private static bool ComputeIsLoading(InfiniteQueryState<TData, TPageParam> state) =>
        state.IsFetching && !state.HasData;

    // Invoked by _stateBinding on the UI thread, once per applied state change; next has already
    // been swapped in as _stateBinding.Current by the time this runs.
    private void ApplyState(InfiniteQueryState<TData, TPageParam> previous, InfiniteQueryState<TData, TPageParam> next)
    {
        RaisePropertyChanged(nameof(CurrentState));

        if (previous.Status != next.Status)
        {
            RaisePropertyChanged(nameof(Status));
        }

        if (previous.IsIdle != next.IsIdle)
        {
            RaisePropertyChanged(nameof(IsIdle));
        }

        if (previous.IsSuccess != next.IsSuccess)
        {
            RaisePropertyChanged(nameof(IsSuccess));
        }

        if (previous.IsFailure != next.IsFailure)
        {
            RaisePropertyChanged(nameof(IsFailure));
        }

        if (!ReferenceEquals(previous.Pages, next.Pages))
        {
            RaisePropertyChanged(nameof(Pages));
        }

        if (!ReferenceEquals(previous.PageParams, next.PageParams))
        {
            RaisePropertyChanged(nameof(PageParams));
        }

        if (previous.HasData != next.HasData)
        {
            RaisePropertyChanged(nameof(HasData));
        }

        if (previous.Error != next.Error)
        {
            RaisePropertyChanged(nameof(Error));
        }

        if (previous.HasError != next.HasError)
        {
            RaisePropertyChanged(nameof(HasError));
        }

        if (previous.HasNextPage != next.HasNextPage)
        {
            RaisePropertyChanged(nameof(HasNextPage));
        }

        if (previous.HasPreviousPage != next.HasPreviousPage)
        {
            RaisePropertyChanged(nameof(HasPreviousPage));
        }

        if (ComputeIsLoading(previous) != ComputeIsLoading(next))
        {
            RaisePropertyChanged(nameof(IsLoading));
        }

        if (previous.IsFetching != next.IsFetching)
        {
            RaisePropertyChanged(nameof(IsFetching));
        }

        if (previous.IsFetchingNextPage != next.IsFetchingNextPage)
        {
            RaisePropertyChanged(nameof(IsFetchingNextPage));
        }

        if (previous.IsFetchingPreviousPage != next.IsFetchingPreviousPage)
        {
            RaisePropertyChanged(nameof(IsFetchingPreviousPage));
        }

        if (IsAnyFetchInFlight(previous) != IsAnyFetchInFlight(next))
        {
            _refetchCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
            _fetchNextPageCommand.RaiseCanExecuteChanged();
            _fetchPreviousPageCommand.RaiseCanExecuteChanged();
        }
        else if (previous.HasNextPage != next.HasNextPage)
        {
            _fetchNextPageCommand.RaiseCanExecuteChanged();
        }
        else if (previous.HasPreviousPage != next.HasPreviousPage)
        {
            _fetchPreviousPageCommand.RaiseCanExecuteChanged();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

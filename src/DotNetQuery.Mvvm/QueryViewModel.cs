namespace DotNetQuery.Mvvm;

/// <summary>
/// An <see cref="INotifyPropertyChanged"/> view model wrapping an <see cref="IQuery{TArgs, TData}"/>
/// for MVVM-based UI frameworks (MAUI, WPF, WinUI, UNO, Avalonia). Exposes the query state as bindable
/// properties and marshals all change notifications onto the UI thread via an
/// <see cref="IUiDispatcher"/>.
/// <para>
/// The view model holds a live subscription to <see cref="IQuery{TArgs, TData}.State"/> for its
/// entire lifetime — this is what keeps the underlying cache entry retained and triggers deferred
/// stale fetches. Dispose the view model when its page is torn down; a forgotten dispose keeps the
/// cache entry alive indefinitely.
/// </para>
/// </summary>
/// <typeparam name="TArgs">The type of the parameters passed to the fetcher.</typeparam>
/// <typeparam name="TData">The type of data returned by the query. Constrained to reference types.</typeparam>
public class QueryViewModel<TArgs, TData> : BindableBase, IDisposable
    where TData : class
{
    private readonly IUiDispatcher _dispatcher;
    private readonly bool _ownsQuery;
    private readonly RelayCommand _refetchCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly IDisposable _subscription;

    private QueryState<TData> _state;
    private QueryState<TData>? _pendingState;
    private bool _isDisposed;

    /// <summary>
    /// Wraps an existing query. The view model does <b>not</b> take ownership:
    /// <see cref="Dispose()"/> releases only the view model's own state subscription,
    /// and the caller remains responsible for disposing <paramref name="query"/>.
    /// </summary>
    /// <param name="query">The query to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public QueryViewModel(IQuery<TArgs, TData> query, IUiDispatcher? dispatcher = null)
        : this(query, ownsQuery: false, dispatcher) { }

    /// <summary>
    /// Creates a new query observer via <see cref="IQueryClient.CreateQuery{TArgs, TData}"/> and
    /// wraps it. The view model owns the created query and disposes it in <see cref="Dispose()"/>.
    /// This is the recommended path for page view models that receive an
    /// <see cref="IQueryClient"/> through dependency injection.
    /// </summary>
    /// <param name="client">The query client used to create the observer.</param>
    /// <param name="options">The query options.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public QueryViewModel(IQueryClient client, QueryOptions<TArgs, TData> options, IUiDispatcher? dispatcher = null)
        : this(CreateQuery(client, options), ownsQuery: true, dispatcher) { }

    private QueryViewModel(IQuery<TArgs, TData> query, bool ownsQuery, IUiDispatcher? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(query);

        Query = query;
        _ownsQuery = ownsQuery;
        _dispatcher = dispatcher ?? SynchronizationContextUiDispatcher.CaptureCurrent();

        // Apply the current state synchronously so bindings evaluated right after construction
        // read correct values without waiting for a dispatcher hop. The replay delivered by the
        // subsequent Subscribe is deduped by the ReferenceEquals guard in ApplyState.
        _state = query.CurrentState;
        _refetchCommand = new RelayCommand(query.Refetch, () => !_state.IsFetching);
        _cancelCommand = new RelayCommand(query.Cancel, () => _state.IsFetching);
        _subscription = query.State.Subscribe(OnStateEmitted);
    }

    /// <summary>
    /// The wrapped query — the escape hatch for members without a bindable counterpart
    /// (<see cref="IQuery.Invalidate"/>, <see cref="IQuery{TArgs, TData}.PrefetchAsync"/>,
    /// <see cref="IQuery{TArgs, TData}.Select{TResult}"/>, or direct Rx composition).
    /// </summary>
    public IQuery<TArgs, TData> Query { get; }

    /// <summary>The raw state snapshot, for bindings and converters that need the whole state.</summary>
    public QueryState<TData> CurrentState => _state;

    /// <summary>The current lifecycle status of the query.</summary>
    public QueryStatus Status => _state.Status;

    /// <summary>
    /// The data returned by the most recent successful fetch; <c>null</c> while fetching.
    /// Bind to <see cref="DisplayData"/> to keep showing stale data during background re-fetches.
    /// </summary>
    public TData? Data => _state.CurrentData;

    /// <summary>The data from the previous successful fetch, carried across fetches and failures.</summary>
    public TData? LastData => _state.LastData;

    /// <summary>
    /// <see cref="Data"/>, falling back to <see cref="LastData"/> while a re-fetch is in progress —
    /// the stale-while-revalidate binding target, mirroring the Blazor <c>&lt;Transition&gt;</c> component.
    /// </summary>
    public TData? DisplayData => _state.CurrentData ?? _state.LastData;

    /// <summary>The exception from the most recent failed fetch. <c>null</c> when not in a failure state.</summary>
    public Exception? Error => _state.Error;

    /// <summary><c>true</c> when the query is idle.</summary>
    public bool IsIdle => _state.IsIdle;

    /// <summary>
    /// <c>true</c> while any fetch is in flight, including background re-fetches.
    /// See <see cref="IsLoading"/> for the first-load-only variant.
    /// </summary>
    public bool IsFetching => _state.IsFetching;

    /// <summary><c>true</c> when the most recent fetch succeeded.</summary>
    public bool IsSuccess => _state.IsSuccess;

    /// <summary><c>true</c> when the most recent fetch failed.</summary>
    public bool IsFailure => _state.IsFailure;

    /// <summary><c>true</c> when <see cref="Data"/> is not <c>null</c>.</summary>
    public bool HasData => _state.HasData;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => _state.HasError;

    /// <summary>
    /// <c>true</c> only during the first load — fetching with no current or previous data to show.
    /// Bind a full-page loading indicator to this and a subtle refresh indicator to
    /// <see cref="IsFetching"/>.
    /// </summary>
    public bool IsLoading => ComputeIsLoading(_state);

    /// <summary>
    /// Triggers <see cref="IQuery.Refetch"/>. Disabled while a fetch is in flight.
    /// </summary>
    public ICommand RefetchCommand => _refetchCommand;

    /// <summary>
    /// Triggers <see cref="IQuery.Cancel"/>. Enabled only while a fetch is in flight.
    /// </summary>
    public ICommand CancelCommand => _cancelCommand;

    /// <inheritdoc cref="IQuery{TArgs, TData}.SetArgs" />
    public void SetArgs(TArgs args) => Query.SetArgs(args);

    /// <inheritdoc cref="IQuery{TArgs, TData}.SetEnabled" />
    public void SetEnabled(bool enabled) => Query.SetEnabled(enabled);

    /// <inheritdoc cref="IQuery{TArgs, TData}.SetData" />
    public void SetData(TData data) => Query.SetData(data);

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
            _subscription.Dispose();

            if (_ownsQuery)
            {
                Query.Dispose();
            }
        }
    }

    private static IQuery<TArgs, TData> CreateQuery(IQueryClient client, QueryOptions<TArgs, TData> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        return client.CreateQuery(options);
    }

    private static bool ComputeIsLoading(QueryState<TData> state) =>
        state.IsFetching && !state.HasData && state.LastData is null;

    // Called on whatever thread the query pushed from. Latest-wins coalescing: a burst of
    // emissions collapses into a single dispatcher post applying only the newest state.
    private void OnStateEmitted(QueryState<TData> state)
    {
        if (Interlocked.Exchange(ref _pendingState, state) is null)
        {
            _dispatcher.Post(DrainPendingState);
        }
    }

    private void DrainPendingState()
    {
        if (Interlocked.Exchange(ref _pendingState, null) is { } state)
        {
            ApplyState(state);
        }
    }

    private void ApplyState(QueryState<TData> next)
    {
        if (_isDisposed || ReferenceEquals(_state, next))
        {
            return;
        }

        // Swap the whole snapshot before raising anything so PropertyChanged handlers reading
        // sibling properties always see a consistent state.
        var previous = _state;
        _state = next;

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

        if (!ReferenceEquals(previous.CurrentData, next.CurrentData))
        {
            RaisePropertyChanged(nameof(Data));
        }

        if (previous.HasData != next.HasData)
        {
            RaisePropertyChanged(nameof(HasData));
        }

        if (!ReferenceEquals(previous.LastData, next.LastData))
        {
            RaisePropertyChanged(nameof(LastData));
        }

        if (!ReferenceEquals(previous.CurrentData ?? previous.LastData, next.CurrentData ?? next.LastData))
        {
            RaisePropertyChanged(nameof(DisplayData));
        }

        if (previous.Error != next.Error)
        {
            RaisePropertyChanged(nameof(Error));
        }

        if (previous.HasError != next.HasError)
        {
            RaisePropertyChanged(nameof(HasError));
        }

        if (ComputeIsLoading(previous) != ComputeIsLoading(next))
        {
            RaisePropertyChanged(nameof(IsLoading));
        }

        if (previous.IsFetching != next.IsFetching)
        {
            RaisePropertyChanged(nameof(IsFetching));
            _refetchCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
        }
    }
}

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
    private readonly bool _ownsQuery;
    private readonly RelayCommand _refetchCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly UiStateBinding<QueryState<TData>> _stateBinding;

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

        // These CanExecute closures read _stateBinding.Current lazily: RelayCommand doesn't invoke
        // canExecute from its own constructor, so by the time anything calls CanExecute, _stateBinding
        // below has long since been assigned — the null-forgiving operator just overrides the
        // compiler's constructor-local flow analysis, which can't see that far ahead.
        // These are created before _stateBinding itself (rather than the other way around) so that
        // if UiStateBinding's constructor happens to invoke ApplyState synchronously — e.g. the
        // dispatcher has no captured SynchronizationContext, as in tests and console apps, so Post
        // runs inline, and the replay Subscribe delivers turns out to differ by reference from
        // "initial" — ApplyState's own use of these fields does not see them unassigned either.
        _refetchCommand = new RelayCommand(query.Refetch, () => !_stateBinding!.Current.IsFetching);
        _cancelCommand = new RelayCommand(query.Cancel, () => _stateBinding!.Current.IsFetching);

        // Apply the current state synchronously so bindings evaluated right after construction
        // read correct values without waiting for a dispatcher hop. See UiStateBinding for how the
        // replay delivered by the subsequent Subscribe is deduped.
        _stateBinding = new UiStateBinding<QueryState<TData>>(
            query.State,
            query.CurrentState,
            dispatcher ?? SynchronizationContextUiDispatcher.CaptureCurrent(),
            ApplyState
        );
    }

    /// <summary>
    /// The wrapped query — the escape hatch for members without a bindable counterpart
    /// (<see cref="IQuery.Invalidate"/>, <see cref="IQuery{TArgs, TData}.PrefetchAsync"/>,
    /// <see cref="IQuery{TArgs, TData}.Select{TResult}"/>, or direct Rx composition).
    /// </summary>
    public IQuery<TArgs, TData> Query { get; }

    /// <summary>The raw state snapshot, for bindings and converters that need the whole state.</summary>
    public QueryState<TData> CurrentState => _stateBinding.Current;

    /// <summary>The current lifecycle status of the query.</summary>
    public QueryStatus Status => _stateBinding.Current.Status;

    /// <summary>
    /// The data returned by the most recent successful fetch; <c>null</c> while fetching.
    /// Bind to <see cref="DisplayData"/> to keep showing stale data during background re-fetches.
    /// </summary>
    public TData? Data => _stateBinding.Current.CurrentData;

    /// <summary>The data from the previous successful fetch, carried across fetches and failures.</summary>
    public TData? LastData => _stateBinding.Current.LastData;

    /// <summary>
    /// <see cref="Data"/>, falling back to <see cref="LastData"/> while a re-fetch is in progress —
    /// the stale-while-revalidate binding target, mirroring the Blazor <c>&lt;Transition&gt;</c> component.
    /// </summary>
    public TData? DisplayData => _stateBinding.Current.CurrentData ?? _stateBinding.Current.LastData;

    /// <summary>The exception from the most recent failed fetch. <c>null</c> when not in a failure state.</summary>
    public Exception? Error => _stateBinding.Current.Error;

    /// <summary><c>true</c> when the query is idle.</summary>
    public bool IsIdle => _stateBinding.Current.IsIdle;

    /// <summary>
    /// <c>true</c> while any fetch is in flight, including background re-fetches.
    /// See <see cref="IsLoading"/> for the first-load-only variant.
    /// </summary>
    public bool IsFetching => _stateBinding.Current.IsFetching;

    /// <summary><c>true</c> when the most recent fetch succeeded.</summary>
    public bool IsSuccess => _stateBinding.Current.IsSuccess;

    /// <summary><c>true</c> when the most recent fetch failed.</summary>
    public bool IsFailure => _stateBinding.Current.IsFailure;

    /// <summary><c>true</c> when <see cref="Data"/> is not <c>null</c>.</summary>
    public bool HasData => _stateBinding.Current.HasData;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => _stateBinding.Current.HasError;

    /// <summary>
    /// <c>true</c> only during the first load — fetching with no current or previous data to show.
    /// Bind a full-page loading indicator to this and a subtle refresh indicator to
    /// <see cref="IsFetching"/>.
    /// </summary>
    public bool IsLoading => ComputeIsLoading(_stateBinding.Current);

    /// <summary>
    /// Triggers <see cref="IQuery.Refetch"/>. Disabled while a fetch is in flight.
    /// </summary>
    public ICommand RefetchCommand => _refetchCommand;

    /// <summary>
    /// Triggers <see cref="IQuery.Cancel"/>. Enabled only while a fetch is in flight.
    /// </summary>
    public ICommand CancelCommand => _cancelCommand;

    /// <summary>
    /// Raised on the UI thread after every <see cref="INotifyPropertyChanged.PropertyChanged"/> notification for an applied
    /// state. Use this — instead of a second raw subscription to <see cref="Query"/>'s state — for
    /// page-specific work such as syncing an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
    /// from <see cref="DisplayData"/>.
    /// </summary>
    public event EventHandler? StateChanged;

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
            _stateBinding.Dispose();

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

    // Invoked by _stateBinding on the UI thread, once per applied state change; next has already
    // been swapped in as _stateBinding.Current by the time this runs.
    private void ApplyState(QueryState<TData> previous, QueryState<TData> next)
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

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace DotNetQuery.Mvvm;

/// <summary>
/// An <see cref="INotifyPropertyChanged"/> view model wrapping an <see cref="IMutation{TArgs, TData}"/>
/// for MVVM-based UI frameworks (MAUI, WPF, WinUI, UNO, Avalonia). Exposes the mutation state as
/// bindable properties and marshals all change notifications onto the UI thread via an
/// <see cref="IUiDispatcher"/>.
/// <para>
/// The view model holds a live subscription to <see cref="IMutation{TArgs, TData}.State"/> for its
/// entire lifetime. Dispose the view model when its page is torn down.
/// </para>
/// <para>
/// <b>Double-submit protection:</b> <see cref="ExecuteCommand"/>, <see cref="Execute"/>, and every
/// command returned by <see cref="ToCommand{TParam}"/> read <see cref="IMutation{TArgs, TData}.CurrentState"/>
/// directly — not the dispatcher-applied <see cref="CurrentState"/> below — before starting a run. That
/// closes the gap between a click starting a run and the resulting state reaching this view model one
/// dispatcher hop later, and it also disables these commands when a run was started directly on a
/// shared <see cref="Mutation"/> by something other than this view model.
/// </para>
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the mutator.</typeparam>
/// <typeparam name="TData">The type of the data returned on success.</typeparam>
public class MutationViewModel<TArgs, TData> : BindableBase, IDisposable
{
    private readonly bool _ownsMutation;
    private readonly MutationExecuteCommand<TArgs> _executeCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly List<Action> _runningDependentRefreshers;
    private readonly UiStateBinding<MutationState<TData>> _stateBinding;

    private bool _isDisposed;

    /// <summary>
    /// Wraps an existing mutation. The view model does <b>not</b> take ownership:
    /// <see cref="Dispose()"/> releases only the view model's own state subscription,
    /// and the caller remains responsible for disposing <paramref name="mutation"/>.
    /// </summary>
    /// <param name="mutation">The mutation to wrap.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public MutationViewModel(IMutation<TArgs, TData> mutation, IUiDispatcher? dispatcher = null)
        : this(mutation, ownsMutation: false, dispatcher) { }

    /// <summary>
    /// Creates a new mutation via <see cref="IQueryClient.CreateMutation{TArgs, TData}"/> and wraps it.
    /// The view model owns the created mutation and disposes it in <see cref="Dispose()"/>. This is the
    /// recommended path for page view models that receive an <see cref="IQueryClient"/> through
    /// dependency injection.
    /// </summary>
    /// <param name="client">The query client used to create the mutation.</param>
    /// <param name="options">The mutation options.</param>
    /// <param name="dispatcher">
    /// The UI-thread dispatcher. When <c>null</c>, <see cref="SynchronizationContext.Current"/> is
    /// captured; construct the view model on the UI thread or pass a dispatcher explicitly.
    /// </param>
    public MutationViewModel(
        IQueryClient client,
        MutationOptions<TArgs, TData> options,
        IUiDispatcher? dispatcher = null
    )
        : this(CreateMutation(client, options), ownsMutation: true, dispatcher) { }

    private MutationViewModel(IMutation<TArgs, TData> mutation, bool ownsMutation, IUiDispatcher? dispatcher)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        Mutation = mutation;
        _ownsMutation = ownsMutation;

        // Created before _stateBinding, and read Mutation.CurrentState directly rather than
        // _stateBinding.Current, so there is no forward reference to work around here (contrast
        // QueryViewModel / InfiniteQueryViewModel) — see the double-submit note on the type itself.
        _executeCommand = new MutationExecuteCommand<TArgs>(
            RunMutation,
            () => !Mutation.CurrentState.IsRunning,
            nameof(ExecuteCommand)
        );
        _cancelCommand = new RelayCommand(mutation.Cancel, () => Mutation.CurrentState.IsRunning);
        _runningDependentRefreshers = [_executeCommand.RaiseCanExecuteChanged, _cancelCommand.RaiseCanExecuteChanged];

        // Apply the current state synchronously so bindings evaluated right after construction
        // read correct values without waiting for a dispatcher hop. See UiStateBinding for how the
        // replay delivered by the subsequent Subscribe is deduped.
        _stateBinding = new UiStateBinding<MutationState<TData>>(
            mutation.State,
            mutation.CurrentState,
            dispatcher ?? SynchronizationContextUiDispatcher.CaptureCurrent(),
            ApplyState
        );
    }

    /// <summary>
    /// The wrapped mutation — the escape hatch for members without a bindable counterpart
    /// (<see cref="IMutation{TArgs, TData}.SetEnabled"/>, or direct Rx composition via
    /// <see cref="IMutation{TArgs, TData}.Success"/>, <see cref="IMutation{TArgs, TData}.Failure"/>,
    /// <see cref="IMutation{TArgs, TData}.Settled"/>).
    /// </summary>
    public IMutation<TArgs, TData> Mutation { get; }

    /// <summary>The raw state snapshot, for bindings and converters that need the whole state.</summary>
    public MutationState<TData> CurrentState => _stateBinding.Current;

    /// <summary>The current lifecycle status of the mutation.</summary>
    public MutationStatus Status => _stateBinding.Current.Status;

    /// <summary>The data returned by the most recent successful execution. <c>null</c> until then.</summary>
    public TData? Data => _stateBinding.Current.CurrentData;

    /// <summary>The exception from the most recent failed execution. <c>null</c> when not in a failure state.</summary>
    public Exception? Error => _stateBinding.Current.Error;

    /// <summary><c>true</c> when the mutation has not yet executed, or has been reset.</summary>
    public bool IsIdle => _stateBinding.Current.IsIdle;

    /// <summary><c>true</c> while an execution is in flight.</summary>
    public bool IsRunning => _stateBinding.Current.IsRunning;

    /// <summary><c>true</c> when the most recent execution succeeded.</summary>
    public bool IsSuccess => _stateBinding.Current.IsSuccess;

    /// <summary><c>true</c> when the most recent execution failed.</summary>
    public bool IsFailure => _stateBinding.Current.IsFailure;

    /// <summary><c>true</c> when <see cref="Data"/> is available.</summary>
    public bool HasData => _stateBinding.Current.HasData;

    /// <summary><c>true</c> when <see cref="Error"/> is not <c>null</c>.</summary>
    public bool HasError => _stateBinding.Current.HasError;

    /// <summary>
    /// Triggers a run with a <c>object?</c> parameter cast strictly to <typeparamref name="TArgs"/>.
    /// Disabled while a run is already in flight. See the type-level remarks for the double-submit
    /// protection this and <see cref="Execute"/> share.
    /// </summary>
    public ICommand ExecuteCommand => _executeCommand;

    /// <summary>
    /// Triggers <see cref="IMutation{TArgs, TData}.Cancel"/>. Enabled only while a run is in flight.
    /// </summary>
    public ICommand CancelCommand => _cancelCommand;

    /// <summary>
    /// Raised on the UI thread after every <see cref="INotifyPropertyChanged.PropertyChanged"/>
    /// notification for an applied state.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Triggers a run with <paramref name="args"/>, unless <see cref="IMutation{TArgs, TData}.CurrentState"/>
    /// already reports <see cref="MutationStatus.Running"/> — see the type-level remarks.
    /// </summary>
    /// <param name="args">The arguments to pass to the mutator.</param>
    public void Execute(TArgs args) => RunMutation(args);

    /// <summary>
    /// Wraps this mutation as a command whose parameter is <typeparamref name="TParam"/>, mapped to
    /// <typeparamref name="TArgs"/> via <paramref name="map"/>. The returned command shares this view
    /// model's double-submit protection, and its <see cref="ICommand.CanExecute"/> additionally
    /// consults <paramref name="canExecute"/> when supplied.
    /// </summary>
    /// <param name="map">Maps the command parameter to the mutator's arguments.</param>
    /// <param name="canExecute">
    /// An additional predicate evaluated with the command parameter. When <c>null</c>, only the
    /// running gate applies. Call <see cref="IMutationCommand.RaiseCanExecuteChanged"/> on the
    /// returned command when an input this predicate reads changes outside the mutation's own state.
    /// </param>
    public IMutationCommand ToCommand<TParam>(Func<TParam, TArgs> map, Func<TParam, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        var command = new MutationMappedCommand<TArgs, TParam>(
            map,
            canExecute,
            () => Mutation.CurrentState.IsRunning,
            RunMutation
        );
        _runningDependentRefreshers.Add(command.RaiseCanExecuteChanged);

        return command;
    }

    /// <summary>
    /// Wraps this mutation as a parameterless command. The mutator's arguments come from
    /// <paramref name="args"/> — typically reading bound properties on the owning page view model —
    /// rather than from the command parameter. The returned command shares this view model's
    /// double-submit protection, and its <see cref="ICommand.CanExecute"/> additionally consults
    /// <paramref name="canExecute"/> when supplied.
    /// </summary>
    /// <param name="args">Produces the mutator's arguments at the moment the command executes.</param>
    /// <param name="canExecute">
    /// An additional predicate. When <c>null</c>, only the running gate applies. Call
    /// <see cref="IMutationCommand.RaiseCanExecuteChanged"/> on the returned command when an input
    /// this predicate reads changes outside the mutation's own state.
    /// </param>
    public IMutationCommand ToCommand(Func<TArgs> args, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = new MutationFactoryCommand<TArgs>(
            args,
            canExecute,
            () => Mutation.CurrentState.IsRunning,
            RunMutation
        );
        _runningDependentRefreshers.Add(command.RaiseCanExecuteChanged);

        return command;
    }

    /// <summary>
    /// Releases the view model's state subscription and disposes the wrapped mutation if this view
    /// model created it.
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

            if (_ownsMutation)
            {
                Mutation.Dispose();
            }
        }
    }

    private static IMutation<TArgs, TData> CreateMutation(IQueryClient client, MutationOptions<TArgs, TData> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        return client.CreateMutation(options);
    }

    // Shared by ExecuteCommand, Execute(TArgs), and every ToCommand-produced command. Guards against
    // double-submit itself — rather than trusting callers to have checked CanExecute first — because
    // a raw ICommand.Execute call (as a fast double-click can produce) does not check CanExecute.
    private void RunMutation(TArgs args)
    {
        if (Mutation.CurrentState.IsRunning)
        {
            return;
        }

        Mutation.Execute(args);
        RefreshRunningDependentCommands();
    }

    private void RefreshRunningDependentCommands()
    {
        foreach (var refresh in _runningDependentRefreshers)
        {
            refresh();
        }
    }

    // Invoked by _stateBinding on the UI thread, once per applied state change; next has already
    // been swapped in as _stateBinding.Current by the time this runs.
    private void ApplyState(MutationState<TData> previous, MutationState<TData> next)
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

        if (!EqualityComparer<TData?>.Default.Equals(previous.CurrentData, next.CurrentData))
        {
            RaisePropertyChanged(nameof(Data));
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

        if (previous.IsRunning != next.IsRunning)
        {
            RaisePropertyChanged(nameof(IsRunning));
            RefreshRunningDependentCommands();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

namespace DotNetQuery.Mvvm;

/// <summary>
/// The commands and double-submit protection shared by <see cref="MutationViewModel{TArgs, TData}"/> and
/// <see cref="MutationViewModel{TArgs, TData, TProgress}"/>, so the mechanic is implemented once rather
/// than per mutation shape. The running gate reads the wrapped mutation's <c>CurrentState</c> directly —
/// see the double-submit note on the view models.
/// </summary>
/// <typeparam name="TArgs">The type of the arguments passed to the mutator.</typeparam>
internal sealed class MutationCommands<TArgs>
{
    private readonly Func<bool> _isRunning;
    private readonly Action<TArgs> _execute;
    private readonly MutationExecuteCommand<TArgs> _executeCommand;
    private readonly RelayCommand _cancelCommand;
    private readonly List<Action> _runningDependentRefreshers;

    /// <param name="isRunning">Reads the wrapped mutation's own <c>CurrentState.IsRunning</c>.</param>
    /// <param name="execute">Starts a run on the wrapped mutation.</param>
    /// <param name="cancel">Cancels the wrapped mutation.</param>
    public MutationCommands(Func<bool> isRunning, Action<TArgs> execute, Action cancel)
    {
        _isRunning = isRunning;
        _execute = execute;
        _executeCommand = new MutationExecuteCommand<TArgs>(Run, () => !isRunning(), "ExecuteCommand");
        _cancelCommand = new RelayCommand(cancel, isRunning);
        _runningDependentRefreshers = [_executeCommand.RaiseCanExecuteChanged, _cancelCommand.RaiseCanExecuteChanged];
    }

    public ICommand ExecuteCommand => _executeCommand;

    public ICommand CancelCommand => _cancelCommand;

    // Shared by ExecuteCommand, the view model's Execute(TArgs), and every ToCommand-produced command.
    // Guards against double-submit itself — rather than trusting callers to have checked CanExecute
    // first — because a raw ICommand.Execute call (as a fast double-click can produce) does not check
    // CanExecute.
    public void Run(TArgs args)
    {
        if (_isRunning())
        {
            return;
        }

        _execute(args);
        RefreshRunningDependentCommands();
    }

    public IMutationCommand ToCommand<TParam>(Func<TParam, TArgs> map, Func<TParam, bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(map);

        var command = new MutationMappedCommand<TArgs, TParam>(map, canExecute, _isRunning, Run);
        _runningDependentRefreshers.Add(command.RaiseCanExecuteChanged);

        return command;
    }

    public IMutationCommand ToCommand(Func<TArgs> args, Func<bool>? canExecute)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = new MutationFactoryCommand<TArgs>(args, canExecute, _isRunning, Run);
        _runningDependentRefreshers.Add(command.RaiseCanExecuteChanged);

        return command;
    }

    /// <summary>Raises <c>CanExecuteChanged</c> on every command gated on the running state. UI thread only.</summary>
    public void RefreshRunningDependentCommands()
    {
        foreach (var refresh in _runningDependentRefreshers)
        {
            refresh();
        }
    }
}

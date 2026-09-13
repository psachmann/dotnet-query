namespace DotNetQuery.Mvvm;

// Backs MutationViewModel.ToCommand(Func<TArgs>, Func<bool>?). Args come from the owning page view
// model rather than the command parameter, so the parameter is ignored entirely.
internal sealed class MutationFactoryCommand<TArgs>(
    Func<TArgs> argsFactory,
    Func<bool>? canExecute,
    Func<bool> isRunning,
    Action<TArgs> execute
) : IMutationCommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isRunning() && (canExecute?.Invoke() ?? true);

    public void Execute(object? parameter) => execute(argsFactory());

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

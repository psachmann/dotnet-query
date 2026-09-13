namespace DotNetQuery.Mvvm;

// Backs MutationViewModel.ExecuteCommand. The parameter is always TArgs — CanExecute depends only
// on whether the mutation is running, never on the parameter itself.
internal sealed class MutationExecuteCommand<TArgs>(Action<TArgs> execute, Func<bool> canExecute, string commandName)
    : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute(CommandParameterCasting.Cast<TArgs>(parameter, commandName));

    // WPF requires CanExecuteChanged to be raised on the UI thread; callers must only
    // invoke this from dispatcher-marshaled code.
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

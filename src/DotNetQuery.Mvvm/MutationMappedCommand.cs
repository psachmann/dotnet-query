namespace DotNetQuery.Mvvm;

// Backs MutationViewModel.ToCommand<TParam>(Func<TParam, TArgs>, Func<TParam, bool>?). The command
// parameter is TParam; it is mapped to TArgs only once the running gate and canExecute have passed.
internal sealed class MutationMappedCommand<TArgs, TParam>(
    Func<TParam, TArgs> map,
    Func<TParam, bool>? canExecute,
    Func<bool> isRunning,
    Action<TArgs> execute
) : IMutationCommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        if (isRunning() || !CommandParameterCasting.TryCast<TParam>(parameter, out var typed))
        {
            return false;
        }

        return canExecute?.Invoke(typed) ?? true;
    }

    public void Execute(object? parameter) =>
        execute(map(CommandParameterCasting.Cast<TParam>(parameter, "ToCommand")));

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

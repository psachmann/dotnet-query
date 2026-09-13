namespace DotNetQuery.Mvvm;

internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter) => execute();

    // WPF requires CanExecuteChanged to be raised on the UI thread; callers must only
    // invoke this from dispatcher-marshaled code.
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

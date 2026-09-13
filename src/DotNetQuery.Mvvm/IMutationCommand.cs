namespace DotNetQuery.Mvvm;

/// <summary>
/// An <see cref="ICommand"/> returned by <see cref="MutationViewModel{TArgs, TData}.ToCommand{TParam}"/>
/// and its parameterless overload. Exposes <see cref="RaiseCanExecuteChanged"/> so a page view model can
/// re-evaluate <see cref="ICommand.CanExecute"/> when an input outside the mutation's own state changes
/// — for example a bound text field that the command's <c>canExecute</c> predicate reads.
/// </summary>
public interface IMutationCommand : ICommand
{
    /// <summary>
    /// Raises <see cref="ICommand.CanExecuteChanged"/>. Call on the UI thread — bound controls
    /// (WPF <c>Button</c>, Avalonia equivalents) re-query <see cref="ICommand.CanExecute"/> in response.
    /// </summary>
    void RaiseCanExecuteChanged();
}

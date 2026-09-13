using System.Windows.Input;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// A single row in the item list. <see cref="ToggleCommand"/> and <see cref="DeleteCommand"/> are the
/// same two shared commands <see cref="TodoDetailsViewModel"/> builds once via <c>ToCommand</c> and
/// passes to every row it creates — there is no back-reference to the owning view model.
/// </summary>
public sealed class TodoItemViewModel(TodoItem model, ICommand toggleCommand, ICommand deleteCommand) : BindableBase
{
    private TodoItem _model = model;

    public TodoItem Model => _model;

    public string Description => _model.Description;

    public bool IsDone => _model.IsDone;

    public ICommand ToggleCommand { get; } = toggleCommand;

    public ICommand DeleteCommand { get; } = deleteCommand;

    /// <summary>
    /// Refreshes this row from a re-fetched <paramref name="model"/> in place, so a bound selection
    /// survives. Called by <see cref="ObservableCollectionExtensions.SyncFrom{TSource, T, TKey}"/>'s
    /// <c>update</c> delegate — see <see cref="TodoDetailsViewModel"/>.
    /// </summary>
    public void Update(TodoItem model)
    {
        if (_model == model)
        {
            return;
        }

        _model = model;
        RaisePropertyChanged(nameof(Model));
        RaisePropertyChanged(nameof(Description));
        RaisePropertyChanged(nameof(IsDone));
    }
}

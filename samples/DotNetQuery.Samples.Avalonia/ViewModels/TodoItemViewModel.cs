using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// A single row in the item list. Commands delegate straight to the owning
/// <see cref="TodoDetailsViewModel"/>, which owns the mutations.
/// </summary>
public sealed class TodoItemViewModel : ObservableObject
{
    private readonly TodoDetailsViewModel _owner;

    public TodoItemViewModel(TodoItem model, TodoDetailsViewModel owner)
    {
        Model = model;
        _owner = owner;
        ToggleCommand = new RelayCommand(() => _owner.ToggleItem(Model));
        DeleteCommand = new RelayCommand(() => _owner.DeleteItem(Model));
    }

    public TodoItem Model { get; }

    public string Description => Model.Description;

    public bool IsDone => Model.IsDone;

    public IRelayCommand ToggleCommand { get; }

    public IRelayCommand DeleteCommand { get; }
}

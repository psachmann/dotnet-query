using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using DotNetQuery.Mvvm;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// The detail pane: the title of the selected list (a plain query) plus its items
/// (an infinite query paged through with "Load more"). Composed entirely from
/// <c>DotNetQuery.Mvvm</c> view models — no hand-rolled loading flags or state tracking.
/// </summary>
public sealed partial class TodoDetailsViewModel : ViewModelBase, IDisposable
{
    private readonly MutationViewModel<TodoItemArgs, Unit> _toggleItem;
    private readonly MutationViewModel<TodoItemArgs, Unit> _deleteItem;
    private readonly ICommand _toggleItemCommand;
    private readonly ICommand _deleteItemCommand;
    private readonly IMutationCommand _addItemCommand;

    private Guid? _listId;

    public TodoDetailsViewModel(TodosQueries queries, TodosMutations mutations)
    {
        // This view model is constructed on the UI thread — it's a constructor parameter of
        // MainViewModel, which App.OnFrameworkInitializationCompleted resolves from DI on the UI
        // thread — so every ToViewModel() call below captures Avalonia's dispatcher via
        // SynchronizationContext.Current without needing to pass one explicitly.
        List = queries.TodoListQuery.ToViewModel();
        Items = queries.TodoItemsInfiniteQuery.ToViewModel();
        AddItem = mutations.AddTodoItem.ToViewModel();

        // Shared across every row: built once here, not per row, so ToCommand's bookkeeping doesn't
        // grow with every item ever created.
        _toggleItem = mutations.ToggleTodoItem.ToViewModel();
        _deleteItem = mutations.DeleteTodoItem.ToViewModel();
        _toggleItemCommand = _toggleItem.ToCommand<TodoItem>(item => new TodoItemArgs(item.Id, item.ListId));
        _deleteItemCommand = _deleteItem.ToCommand<TodoItem>(item => new TodoItemArgs(item.Id, item.ListId));

        _addItemCommand = AddItem.ToCommand(BuildAddItemArgs, CanAddItem);

        Items.StateChanged += (_, _) => SyncItemRows();
    }

    public QueryViewModel<Guid, TodoList> List { get; }

    public InfiniteQueryViewModel<Guid, List<TodoItem>, int> Items { get; }

    public MutationViewModel<AddTodoItemArgs, TodoItem> AddItem { get; }

    public ICommand AddItemCommand => _addItemCommand;

    public ObservableCollection<TodoItemViewModel> ItemRows { get; } = [];

    [ObservableProperty]
    public partial bool HasList { get; private set; }

    [ObservableProperty]
    public partial string NewItemDescription { get; set; } = string.Empty;

    /// <summary>
    /// Points both queries at <paramref name="listId"/>. Pushing new args is all it takes —
    /// the observers switch to the cache entry for the new key and fetch only if needed.
    /// </summary>
    public void SetList(Guid? listId)
    {
        if (_listId == listId)
        {
            return;
        }

        _listId = listId;
        HasList = listId.HasValue;
        NewItemDescription = string.Empty;
        _addItemCommand.RaiseCanExecuteChanged();

        if (listId is not { } id)
        {
            ItemRows.Clear();
            return;
        }

        List.SetArgs(id);
        Items.SetArgs(id);
    }

    public void Dispose()
    {
        List.Dispose();
        Items.Dispose();
        AddItem.Dispose();
        _toggleItem.Dispose();
        _deleteItem.Dispose();
    }

    partial void OnNewItemDescriptionChanged(string value) => _addItemCommand.RaiseCanExecuteChanged();

    private AddTodoItemArgs BuildAddItemArgs()
    {
        var args = new AddTodoItemArgs(_listId!.Value, NewItemDescription.Trim());

        // Optimistic clear, mirrors the original hand-written command's behavior: the field empties
        // immediately on submit rather than waiting for the mutation to settle.
        NewItemDescription = string.Empty;

        return args;
    }

    private bool CanAddItem() => _listId is not null && !string.IsNullOrWhiteSpace(NewItemDescription);

    private void SyncItemRows() =>
        ItemRows.SyncFrom(
            Items.Pages.SelectMany(page => page),
            model => model.Id,
            row => row.Model.Id,
            model => new TodoItemViewModel(model, _toggleItemCommand, _deleteItemCommand),
            (row, model) => row.Update(model)
        );
}

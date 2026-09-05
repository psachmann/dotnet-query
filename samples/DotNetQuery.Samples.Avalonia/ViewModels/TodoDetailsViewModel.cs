using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// The detail pane: the title of the selected list (a plain query) plus its items
/// (an infinite query paged through with "Load more").
/// </summary>
public sealed partial class TodoDetailsViewModel : ViewModelBase, IDisposable
{
    private readonly TodosQueries _queries;
    private readonly TodosMutations _mutations;
    private readonly CompositeDisposable _subscriptions = [];

    private Guid? _listId;

    public TodoDetailsViewModel(TodosQueries queries, TodosMutations mutations)
    {
        _queries = queries;
        _mutations = mutations;

        // Both queries push their state from whatever thread the fetch completed on,
        // so everything is marshalled onto the UI thread before it touches a bound property.
        _subscriptions.Add(_queries.TodoListQuery.State.SubscribeOnUiThread(ApplyListState));
        _subscriptions.Add(_queries.TodoItemsInfiniteQuery.State.SubscribeOnUiThread(ApplyItemsState));
    }

    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    [ObservableProperty]
    public partial bool HasList { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoadingTitle { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingItems { get; private set; }

    [ObservableProperty]
    public partial bool IsLoadingMore { get; private set; }

    [ObservableProperty]
    public partial bool HasNextPage { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
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

        if (listId is not { } id)
        {
            Title = string.Empty;
            Items.Clear();
            HasNextPage = false;
            return;
        }

        _queries.TodoListQuery.SetArgs(id);
        _queries.TodoItemsInfiniteQuery.SetArgs(id);
    }

    public void ToggleItem(TodoItem item) => _mutations.ToggleTodoItem.Execute(new TodoItemArgs(item.Id, item.ListId));

    public void DeleteItem(TodoItem item) => _mutations.DeleteTodoItem.Execute(new TodoItemArgs(item.Id, item.ListId));

    public void Dispose() => _subscriptions.Dispose();

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        if (_listId is not { } id || string.IsNullOrWhiteSpace(NewItemDescription))
        {
            return;
        }

        _mutations.AddTodoItem.Execute(new AddTodoItemArgs(id, NewItemDescription.Trim()));
        NewItemDescription = string.Empty;
    }

    private bool CanAddItem() => !string.IsNullOrWhiteSpace(NewItemDescription);

    [RelayCommand]
    private void LoadMore() => _queries.TodoItemsInfiniteQuery.FetchNextPage();

    private void ApplyListState(QueryState<TodoList> state)
    {
        // Transition semantics: keep showing the previous title while a re-fetch is running.
        IsLoadingTitle = state.IsFetching && state.LastData is null;
        Title = (state.CurrentData ?? state.LastData)?.Title ?? string.Empty;
    }

    private void ApplyItemsState(InfiniteQueryState<List<TodoItem>, int> state)
    {
        IsLoadingItems = state.IsFetching && !state.HasData;
        IsLoadingMore = state.IsFetchingNextPage;
        HasNextPage = state.HasNextPage;
        ErrorMessage = state.Error?.Message;

        SyncItems(state.Pages.SelectMany(page => page).ToList());
    }

    private void SyncItems(IReadOnlyList<TodoItem> items)
    {
        // TodoItem is a record, so value equality is enough to skip a rebuild on
        // re-fetches that returned the same data.
        if (Items.Count == items.Count && Items.Select(vm => vm.Model).SequenceEqual(items))
        {
            return;
        }

        Items.Clear();

        foreach (var item in items)
        {
            Items.Add(new TodoItemViewModel(item, this));
        }
    }
}

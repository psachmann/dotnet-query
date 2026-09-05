using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// The shell: a sidebar driven by <c>TodoListsQuery</c> and a detail pane whose args are
/// pushed from the current selection.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IQueryClient _queryClient;
    private readonly TodosQueries _queries;
    private readonly TodosMutations _mutations;
    private readonly CompositeDisposable _subscriptions = [];

    private Guid? _pendingSelectionId;
    private bool _isSyncingLists;
    private int _listCounter;

    public MainViewModel(
        IQueryClient queryClient,
        TodosQueries queries,
        TodosMutations mutations,
        TodoDetailsViewModel details
    )
    {
        _queryClient = queryClient;
        _queries = queries;
        _mutations = mutations;
        Details = details;

        _subscriptions.Add(_queries.TodoListsQuery.State.SubscribeOnUiThread(ApplyState));

        // The mutation invalidates "todo-lists" on success; remember the new id so the refreshed
        // sidebar can select it.
        _subscriptions.Add(
            _mutations.CreateTodoList.Success.SubscribeOnUiThread(list => _pendingSelectionId = list.Id)
        );
    }

    public TodoDetailsViewModel Details { get; }

    public ObservableCollection<TodoList> Lists { get; } = [];

    [ObservableProperty]
    public partial bool IsSidebarExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLoadingLists { get; private set; }

    [ObservableProperty]
    public partial TodoList? SelectedList { get; set; }

    public void Dispose() => _subscriptions.Dispose();

    partial void OnSelectedListChanged(TodoList? value)
    {
        // Clearing the collection makes the ListBox push a null selection back; ignore it so the
        // detail pane isn't torn down and rebuilt on every background re-fetch.
        if (_isSyncingLists)
        {
            return;
        }

        Details.SetList(value?.Id);
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    [RelayCommand]
    private void CreateList() => _mutations.CreateTodoList.Execute($"New List {++_listCounter}");

    /// <summary>
    /// The desktop counterpart of the Blazor sample's <c>&lt;QueryRefreshMonitor&gt;</c>:
    /// marks every cache entry stale, which re-fetches the ones that currently have subscribers.
    /// </summary>
    [RelayCommand]
    private void Refresh() => _queryClient.Invalidate(_ => true);

    private void ApplyState(QueryState<List<TodoList>> state)
    {
        IsLoadingLists = state.IsFetching && state.LastData is null;

        // Transition semantics: keep the previous lists on screen during a background re-fetch.
        if ((state.CurrentData ?? state.LastData) is { } lists)
        {
            ApplyLists(lists);
        }
    }

    private void ApplyLists(IReadOnlyList<TodoList> lists)
    {
        var selectedId = _pendingSelectionId ?? SelectedList?.Id;
        _pendingSelectionId = null;

        // TodoList is a record, so value equality is enough to skip a rebuild on
        // re-fetches that returned the same data.
        if (!Lists.SequenceEqual(lists))
        {
            _isSyncingLists = true;
            Lists.Clear();

            foreach (var list in lists)
            {
                Lists.Add(list);
            }

            _isSyncingLists = false;
        }

        SelectedList = lists.FirstOrDefault(list => list.Id == selectedId) ?? lists.FirstOrDefault();
    }
}

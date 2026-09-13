using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetQuery.Mvvm;

namespace DotNetQuery.Samples.Avalonia.ViewModels;

/// <summary>
/// The shell: a sidebar driven by a <see cref="QueryViewModel{TArgs, TData}"/> and a detail pane
/// whose args are pushed from the current selection.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly IQueryClient _queryClient;
    private readonly TodosMutations _mutations;
    private readonly CompositeDisposable _subscriptions = [];

    private Guid? _pendingSelectionId;
    private int _listCounter;

    public MainViewModel(
        IQueryClient queryClient,
        TodosQueries queries,
        TodosMutations mutations,
        TodoDetailsViewModel details
    )
    {
        // Resolved from DI in App.OnFrameworkInitializationCompleted, on the UI thread, so ToViewModel()
        // below captures Avalonia's dispatcher via SynchronizationContext.Current without an explicit
        // dispatcher argument. TodoDetailsViewModel (a constructor parameter here) relies on the same
        // thing.
        _queryClient = queryClient;
        _mutations = mutations;
        Details = details;

        TodoLists = queries.TodoListsQuery.ToViewModel();
        TodoLists.StateChanged += (_, _) =>
        {
            if (TodoLists.DisplayData is { } lists)
            {
                ApplyLists(lists);
            }
        };

        // The mutation invalidates "todo-lists" on success; remember the new id so the refreshed
        // sidebar can select it. Success is an event stream, not a state snapshot — ObserveOnUi,
        // not TodoLists.StateChanged's coalescing, since missing an emission here would leave the
        // wrong list selected.
        _subscriptions.Add(
            _mutations.CreateTodoList.Success.ObserveOnUi().Subscribe(list => _pendingSelectionId = list.Id)
        );
    }

    public TodoDetailsViewModel Details { get; }

    public QueryViewModel<Unit, List<TodoList>> TodoLists { get; }

    public ObservableCollection<TodoList> Lists { get; } = [];

    [ObservableProperty]
    public partial bool IsSidebarExpanded { get; set; } = true;

    [ObservableProperty]
    public partial TodoList? SelectedList { get; set; }

    public void Dispose()
    {
        TodoLists.Dispose();
        _subscriptions.Dispose();
    }

    partial void OnSelectedListChanged(TodoList? value) => Details.SetList(value?.Id);

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

    private void ApplyLists(IReadOnlyList<TodoList> lists)
    {
        var selectedId = _pendingSelectionId ?? SelectedList?.Id;
        _pendingSelectionId = null;

        Lists.SyncFrom(lists, list => list.Id);

        SelectedList = Lists.FirstOrDefault(list => list.Id == selectedId) ?? Lists.FirstOrDefault();
    }
}

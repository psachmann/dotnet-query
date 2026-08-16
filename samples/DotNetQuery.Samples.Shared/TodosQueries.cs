using System.Reactive;

namespace DotNetQuery.Samples.Shared;

public sealed class TodosQueries : IDisposable
{
    public const int PageSize = 5;

    public readonly IQuery<Unit, List<TodoList>> TodoListsQuery;
    public readonly IInfiniteQuery<Guid, List<TodoItem>, int> TodoItemsInfiniteQuery;

    public TodosQueries(IQueryClient queryClient, ITodosClient todosClient)
    {
        TodoListsQuery = queryClient.CreateQuery(
            new QueryOptions<Unit, List<TodoList>>
            {
                KeyFactory = _ => ["todo-lists"],
                Fetcher = (_, ct) => todosClient.GetTodoListsAsync(ct),
            }
        );
        TodoListsQuery.SetArgs(Unit.Default);

        TodoItemsInfiniteQuery = queryClient.CreateInfiniteQuery(
            new InfiniteQueryOptions<Guid, List<TodoItem>, int>
            {
                KeyFactory = listId => ["todo-items", listId],
                Fetcher = (listId, page, ct) => todosClient.GetTodoItemsPagedAsync(listId, page, PageSize, ct),
                InitialPageParam = 1,
                GetNextPageParam = info =>
                    info.Page.Count < PageSize ? PageParam<int>.None : PageParam<int>.Some(info.PageParam + 1),
            }
        );
    }

    public void Dispose()
    {
        TodoListsQuery.Dispose();
        TodoItemsInfiniteQuery.Dispose();
    }
}

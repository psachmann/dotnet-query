using System.Reactive;

namespace DotNetQuery.Samples.Shared;

public sealed record UpdateTodoListArgs(Guid ListId, string Title);

public sealed record AddTodoItemArgs(Guid ListId, string Description);

public sealed record UpdateTodoItemArgs(Guid ItemId, Guid ListId, string Description);

public sealed record TodoItemArgs(Guid ItemId, Guid ListId);

public sealed class TodosMutations : IDisposable
{
    public readonly IMutation<string, TodoList> CreateTodoList;
    public readonly IMutation<UpdateTodoListArgs, Unit> UpdateTodoList;
    public readonly IMutation<Guid, Unit> DeleteTodoList;
    public readonly IMutation<AddTodoItemArgs, TodoItem> AddTodoItem;
    public readonly IMutation<TodoItemArgs, Unit> ToggleTodoItem;
    public readonly IMutation<UpdateTodoItemArgs, Unit> UpdateTodoItem;
    public readonly IMutation<TodoItemArgs, Unit> DeleteTodoItem;

    public TodosMutations(IQueryClient queryClient, ITodosClient todosClient)
    {
        CreateTodoList = queryClient.CreateMutation(
            new MutationOptions<string, TodoList>
            {
                Mutator = (title, ct) => todosClient.CreateTodoListAsync(title, ct),
                InvalidateKeys = [
                    ["todo-lists"]
                ],
            }
        );

        UpdateTodoList = queryClient.CreateMutation(
            new MutationOptions<UpdateTodoListArgs, Unit>
            {
                Mutator = async (args, ct) =>
                {
                    await todosClient.UpdateTodoListAsync(args.ListId, args.Title, ct);
                    return Unit.Default;
                },
                InvalidateKeys =
                [
                    ["todo-lists"],
                ],
            }
        );

        DeleteTodoList = queryClient.CreateMutation(
            new MutationOptions<Guid, Unit>
            {
                Mutator = async (listId, ct) =>
                {
                    await todosClient.DeleteTodoListAsync(listId, ct);
                    return Unit.Default;
                },
                InvalidateKeys =
                [
                    ["todo-lists"],
                ],
            }
        );

        AddTodoItem = queryClient.CreateMutation(
            new MutationOptions<AddTodoItemArgs, TodoItem>
            {
                Mutator = (args, ct) => todosClient.AddTodoItemAsync(args.ListId, args.Description, ct),
                OnSuccess = (args, _) => queryClient.Invalidate(["todo-items", args.ListId]),
            }
        );

        ToggleTodoItem = queryClient.CreateMutation(
            new MutationOptions<TodoItemArgs, Unit>
            {
                Mutator = async (args, ct) =>
                {
                    await todosClient.ToggleTodoItemAsync(args.ItemId, ct);
                    return Unit.Default;
                },
                OnSuccess = (args, _) => queryClient.Invalidate(["todo-items", args.ListId]),
            }
        );

        UpdateTodoItem = queryClient.CreateMutation(
            new MutationOptions<UpdateTodoItemArgs, Unit>
            {
                Mutator = async (args, ct) =>
                {
                    await todosClient.UpdateTodoItemAsync(args.ItemId, args.Description, ct);
                    return Unit.Default;
                },
                OnSuccess = (args, _) => queryClient.Invalidate(["todo-items", args.ListId]),
            }
        );

        DeleteTodoItem = queryClient.CreateMutation(
            new MutationOptions<TodoItemArgs, Unit>
            {
                Mutator = async (args, ct) =>
                {
                    await todosClient.DeleteTodoItemAsync(args.ItemId, ct);
                    return Unit.Default;
                },
                OnSuccess = (args, _) => queryClient.Invalidate(["todo-items", args.ListId]),
            }
        );
    }

    public void Dispose()
    {
        CreateTodoList.Dispose();
        UpdateTodoList.Dispose();
        DeleteTodoList.Dispose();
        AddTodoItem.Dispose();
        ToggleTodoItem.Dispose();
        UpdateTodoItem.Dispose();
        DeleteTodoItem.Dispose();
    }
}

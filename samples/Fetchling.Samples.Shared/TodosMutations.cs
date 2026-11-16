using System.Reactive;

namespace Fetchling.Samples.Shared;

public sealed record UpdateTodoListArgs(Guid ListId, string Title);

public sealed record AddTodoItemArgs(Guid ListId, string Description);

public sealed record UpdateTodoItemArgs(Guid ItemId, Guid ListId, string Description);

public sealed record TodoItemArgs(Guid ItemId, Guid ListId);

public sealed class TodosMutations : IDisposable
{
    public IMutation<string, TodoList> CreateTodoList { get; }
    public IMutation<UpdateTodoListArgs, Unit> UpdateTodoList { get; }
    public IMutation<Guid, Unit> DeleteTodoList { get; }
    public IMutation<AddTodoItemArgs, TodoItem> AddTodoItem { get; }
    public IMutation<TodoItemArgs, Unit> ToggleTodoItem { get; }
    public IMutation<UpdateTodoItemArgs, Unit> UpdateTodoItem { get; }
    public IMutation<TodoItemArgs, Unit> DeleteTodoItem { get; }

    // This class owns every mutation it creates and disposes them in Dispose().
    // Invalidation: InvalidateKeys for fixed keys (todo-lists), OnSuccess when the key depends on the args (todo-items per list).
    public TodosMutations(IQueryClient queryClient, ITodosClient todosClient)
    {
        CreateTodoList = queryClient.CreateMutation(
            new MutationOptions<string, TodoList>
            {
                Mutator = (title, ct) => todosClient.CreateTodoListAsync(title, ct),
                InvalidateKeys =
                [
                    ["todo-lists"],
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

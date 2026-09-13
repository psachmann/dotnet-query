namespace DotNetQuery.Samples.Shared;

public interface ITodosClient
{
    Task<TodoList> CreateTodoListAsync(string title, CancellationToken cts = default);

    Task<TodoList> GetTodoListByIdAsync(Guid id, CancellationToken cts = default);

    Task<List<TodoList>> GetTodoListsAsync(CancellationToken cts = default);

    Task UpdateTodoListAsync(Guid listId, string title, CancellationToken cts = default);

    Task DeleteTodoListAsync(Guid listId, CancellationToken cts = default);

    Task<List<TodoItem>> GetTodoItemsAsync(Guid listId, CancellationToken cts = default);

    Task<List<TodoItem>> GetTodoItemsPagedAsync(Guid listId, int page, int pageSize, CancellationToken cts = default);

    Task<TodoItem> AddTodoItemAsync(Guid listId, string description, CancellationToken cts = default);

    Task ToggleTodoItemAsync(Guid itemId, CancellationToken cts = default);

    Task UpdateTodoItemAsync(Guid itemId, string description, CancellationToken cts = default);

    Task DeleteTodoItemAsync(Guid itemId, CancellationToken cts = default);
}

internal sealed class TodosClientImpl(TodosContext context) : ITodosClient, IDisposable
{
    public async Task<TodoList> CreateTodoListAsync(string title, CancellationToken cts = default)
    {
        var list = new TodoList { Title = title };
        context.Set<TodoList>().Add(list);
        await context.SaveChangesAsync(cts);

        // Detached copy: the context stays open (and tracking this entity) for the app's whole
        // lifetime, so returning the tracked instance itself would let a later mutation on the same
        // row silently mutate whatever earlier query result the caller is still holding onto.
        return list with
        { };
    }

    public Task<TodoList> GetTodoListByIdAsync(Guid id, CancellationToken cts = default) =>
        context.Set<TodoList>().AsNoTracking().Where(list => list.Id == id).FirstAsync(cts);

    public Task<List<TodoList>> GetTodoListsAsync(CancellationToken cts = default) =>
        context.Set<TodoList>().AsNoTracking().OrderBy(l => l.CreatedAt).ToListAsync(cts);

    public async Task UpdateTodoListAsync(Guid listId, string title, CancellationToken cts = default)
    {
        var list =
            await context.Set<TodoList>().FindAsync([listId], cts)
            ?? throw new KeyNotFoundException($"TodoList {listId} not found.");

        context.Entry(list).CurrentValues.SetValues(list with { Title = title });
        await context.SaveChangesAsync(cts);
    }

    public async Task DeleteTodoListAsync(Guid listId, CancellationToken cts = default)
    {
        var list =
            await context.Set<TodoList>().FindAsync([listId], cts)
            ?? throw new KeyNotFoundException($"TodoList {listId} not found.");

        context.Set<TodoList>().Remove(list);
        await context.SaveChangesAsync(cts);
    }

    public Task<List<TodoItem>> GetTodoItemsAsync(Guid listId, CancellationToken cts = default) =>
        context
            .Set<TodoItem>()
            .AsNoTracking()
            .Where(i => i.ListId == listId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cts);

    public Task<List<TodoItem>> GetTodoItemsPagedAsync(
        Guid listId,
        int page,
        int pageSize,
        CancellationToken cts = default
    ) =>
        context
            .Set<TodoItem>()
            .AsNoTracking()
            .Where(i => i.ListId == listId)
            .OrderBy(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cts);

    public async Task<TodoItem> AddTodoItemAsync(Guid listId, string description, CancellationToken cts = default)
    {
        var item = new TodoItem { ListId = listId, Description = description };
        context.Set<TodoItem>().Add(item);
        await context.SaveChangesAsync(cts);

        // Detached copy -- see CreateTodoListAsync.
        return item with
        { };
    }

    public async Task ToggleTodoItemAsync(Guid itemId, CancellationToken cts = default)
    {
        var item =
            await context.Set<TodoItem>().FindAsync([itemId], cts)
            ?? throw new KeyNotFoundException($"TodoItem {itemId} not found.");

        context.Entry(item).CurrentValues.SetValues(item with { IsDone = !item.IsDone });
        await context.SaveChangesAsync(cts);
    }

    public async Task UpdateTodoItemAsync(Guid itemId, string description, CancellationToken cts = default)
    {
        var item =
            await context.Set<TodoItem>().FindAsync([itemId], cts)
            ?? throw new KeyNotFoundException($"TodoItem {itemId} not found.");

        context.Entry(item).CurrentValues.SetValues(item with { Description = description });
        await context.SaveChangesAsync(cts);
    }

    public async Task DeleteTodoItemAsync(Guid itemId, CancellationToken cts = default)
    {
        var item =
            await context.Set<TodoItem>().FindAsync([itemId], cts)
            ?? throw new KeyNotFoundException($"TodoItem {itemId} not found.");

        context.Set<TodoItem>().Remove(item);
        await context.SaveChangesAsync(cts);
    }

    public void Dispose() => context.Dispose();
}

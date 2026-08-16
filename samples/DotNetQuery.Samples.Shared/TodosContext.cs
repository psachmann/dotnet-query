namespace DotNetQuery.Samples.Shared;

internal sealed class TodosContext : DbContext
{
    public DbSet<TodoList>? TodoLists { get; init; }

    public DbSet<TodoItem>? TodoItems { get; init; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase("todos-db");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TodoListConfiguration());
        modelBuilder.ApplyConfiguration(new TodoItemConfiguration());
    }
}

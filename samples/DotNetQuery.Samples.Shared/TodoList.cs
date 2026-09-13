namespace DotNetQuery.Samples.Shared;

public record TodoList
{
    public Guid Id { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAg { get; init; }

    public string Title { get; init; } = string.Empty;
}

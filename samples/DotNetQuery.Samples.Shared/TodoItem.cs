namespace DotNetQuery.Samples.Shared;

public record TodoItem
{
    public Guid Id { get; init; }

    public Guid ListId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAg { get; init; }

    public string Description { get; init; } = string.Empty;

    public bool IsDone { get; init; }
}

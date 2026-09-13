using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetQuery.Samples.Shared;

internal sealed class TodoListConfiguration : IEntityTypeConfiguration<TodoList>
{
    public void Configure(EntityTypeBuilder<TodoList> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id).ValueGeneratedOnAdd();

        builder.Property(l => l.Title).IsRequired().HasMaxLength(200);

        builder.Property(l => l.CreatedAt).ValueGeneratedOnAdd().HasValueGenerator<UtcNowValueGenerator>();

        builder.Property(l => l.UpdatedAg).ValueGeneratedOnAddOrUpdate().HasValueGenerator<UtcNowValueGenerator>();

        builder.HasMany<TodoItem>().WithOne().HasForeignKey(i => i.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}

using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetQuery.Samples.Shared;

internal sealed class TodoItemConfiguration : IEntityTypeConfiguration<TodoItem>
{
    public void Configure(EntityTypeBuilder<TodoItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.ListId).IsRequired();

        builder.Property(i => i.Description).IsRequired().HasMaxLength(500);

        builder.Property(i => i.IsDone).IsRequired();

        builder.Property(i => i.CreatedAt).ValueGeneratedOnAdd().HasValueGenerator<UtcNowValueGenerator>();

        builder.Property(i => i.UpdatedAg).ValueGeneratedOnAddOrUpdate().HasValueGenerator<UtcNowValueGenerator>();

        builder.HasIndex(i => i.ListId);
    }
}

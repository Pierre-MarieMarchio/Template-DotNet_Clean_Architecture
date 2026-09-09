using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;

/// <summary>
/// Mapping for an item row: a child of the to-do list root, with its own table but no
/// <see cref="DbSet{TEntity}"/> — the only way in is through the root, exactly as in the domain.
/// </summary>
internal sealed class TodoItemRecordConfiguration : IEntityTypeConfiguration<TodoItemRecord>
{
    public void Configure(EntityTypeBuilder<TodoItemRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TodoItems", AppDbContext.TodoSchema);

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.TodoListId).IsRequired();

        builder.Property(item => item.Title)
            .HasMaxLength(TodoItemTitle.MaxLength)
            .IsRequired();

        builder.Property(item => item.Description)
            .HasMaxLength(TodoItem.MaxDescriptionLength);

        builder.Property(item => item.CompletedAt);

        // No unique index on (TodoListId, Title): the domain rule is case-insensitive and a B-tree
        // unique index is not, so it would enforce a weaker second rule. The aggregate enforces the
        // real one, because a write always loads every item.
        builder.HasIndex(item => item.TodoListId);

        builder.HasMany(item => item.Tags)
            .WithOne()
            .HasForeignKey(tag => tag.TodoItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Configurations;

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

        builder.HasIndex(item => item.TodoListId);

        builder.HasMany(item => item.Tags)
            .WithOne()
            .HasForeignKey(tag => tag.TodoItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;

/// <summary>
/// Mapping for a tag row. The key is the pair, not a surrogate: a tag has no identity of its own, so
/// the thing that identifies the row is the owner plus the value — and that makes a duplicate tag on
/// one item impossible in the database rather than merely unlikely.
/// </summary>
internal sealed class TodoItemTagRecordConfiguration : IEntityTypeConfiguration<TodoItemTagRecord>
{
    public void Configure(EntityTypeBuilder<TodoItemTagRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TodoItemTags", AppDbContext.TodoSchema);

        builder.HasKey(tag => new { tag.TodoItemId, tag.Value });

        builder.Property(tag => tag.Value)
            .HasMaxLength(Tag.MaxLength)
            .IsRequired();
    }
}

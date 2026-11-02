using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Configurations;

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

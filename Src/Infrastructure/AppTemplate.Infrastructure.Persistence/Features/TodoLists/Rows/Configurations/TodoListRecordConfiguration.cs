using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Configurations;

internal sealed class TodoListRecordConfiguration : IEntityTypeConfiguration<TodoListRecord>
{
    public void Configure(EntityTypeBuilder<TodoListRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TodoLists", AppDbContext.TodoSchema);

        builder.HasKey(list => list.Id);

        builder.Property(list => list.Id).ValueGeneratedNever();

        builder.Property(list => list.OwnerId).IsRequired();

        builder.HasIndex(list => new { list.OwnerId, list.Name, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_Name_Id");

        builder.HasIndex(list => new { list.OwnerId, list.CreatedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_CreatedAt_Id");

        builder.HasIndex(list => new { list.OwnerId, list.LastModifiedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_LastModifiedAt_Id");

        builder.Property(list => list.Name)
            .HasMaxLength(TodoListName.MaxLength)
            .IsRequired();

        builder.Property(list => list.Version).IsVersion();

        builder.Property(list => list.CreatedAt).IsRequired();
        builder.Property(list => list.CreatedBy);
        builder.Property(list => list.LastModifiedAt);
        builder.Property(list => list.LastModifiedBy);

        builder.HasMany(list => list.Items)
            .WithOne()
            .HasForeignKey(item => item.TodoListId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

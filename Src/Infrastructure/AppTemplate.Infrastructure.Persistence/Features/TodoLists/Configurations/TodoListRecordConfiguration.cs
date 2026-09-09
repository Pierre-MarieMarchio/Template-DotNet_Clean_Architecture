using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;

/// <summary>
/// Mapping for the to-do list root row: column lengths, indexes and the concurrency token.
/// <para>
/// It reads the domain's constants for lengths — <see cref="TodoListName.MaxLength"/> — so the
/// column and the invariant cannot drift apart. That is the one direction in which a schema should
/// depend on a domain: the rule is stated once, in the model, and the database enforces the same
/// number rather than a copy of it.
/// </para>
/// </summary>
internal sealed class TodoListRecordConfiguration : IEntityTypeConfiguration<TodoListRecord>
{
    public void Configure(EntityTypeBuilder<TodoListRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // This context holds two schemas, so every table names its own rather than inheriting one.
        builder.ToTable("TodoLists", AppDbContext.TodoSchema);

        builder.HasKey(list => list.Id);

        // Ids are UUIDv7, created by the domain: sequential enough to keep index inserts local, and
        // known before the insert.
        builder.Property(list => list.Id).ValueGeneratedNever();

        builder.Property(list => list.OwnerId).IsRequired();

        // One index per sortable field, leading with OwnerId, which every read filters by, and
        // ending in Id, the tiebreaker TodoListSortMap always appends. A field made sortable is a
        // field that gets an index.
        builder.HasIndex(list => new { list.OwnerId, list.Name, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_Name_Id");

        builder.HasIndex(list => new { list.OwnerId, list.CreatedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_CreatedAt_Id");

        builder.HasIndex(list => new { list.OwnerId, list.LastModifiedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_LastModifiedAt_Id");

        builder.Property(list => list.Name)
            .HasMaxLength(TodoListName.MaxLength)
            .IsRequired();

        // PostgreSQL's xmin system column, which already exists: EF reads it back after a write and
        // puts it in the WHERE clause of the next one. On the root only — a concurrent edit to any
        // item is a conflict on the list.
        builder.Property(list => list.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(list => list.CreatedAt).IsRequired();
        builder.Property(list => list.CreatedBy);
        builder.Property(list => list.LastModifiedAt);
        builder.Property(list => list.LastModifiedBy);

        // Cascade: an item has no meaning without its list, and surviving items would be rows no
        // code path can reach.
        builder.HasMany(list => list.Items)
            .WithOne()
            .HasForeignKey(item => item.TodoListId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}

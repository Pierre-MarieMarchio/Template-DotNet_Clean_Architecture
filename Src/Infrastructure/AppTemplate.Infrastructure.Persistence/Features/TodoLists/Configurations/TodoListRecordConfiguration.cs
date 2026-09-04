using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;

/// <summary>
/// Mapping for the to-do list root row. The previous model had no configuration classes at all, so
/// every string column was <c>text</c> with no length, nothing was indexed except the primary keys,
/// and there was no concurrency control.
/// <para>
/// It still reads the domain's constants for lengths — <see cref="TodoListName.MaxLength"/> — so the
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

        // The schema is named here rather than as a context-wide default, because this context holds
        // two schemas. Every table states its own, so none can drift into the wrong one by omission.
        builder.ToTable("TodoLists", AppDbContext.TodoSchema);

        builder.HasKey(list => list.Id);

        // Ids are UUIDv7, created by the domain: sequential enough to keep index inserts
        // local, and known before the insert so a use case can return one without a
        // round trip. The database must therefore not try to generate them.
        builder.Property(list => list.Id).ValueGeneratedNever();

        builder.Property(list => list.OwnerId).IsRequired();

        // One composite index per sortable field, each leading with OwnerId — every read filters by
        // it — and ending in Id, the same tiebreaker TodoListSortMap always appends, so both the
        // ORDER BY and the keyset comparison that resumes it stay index-ordered instead of falling
        // back to a sort. A field made sortable is a field that gets an index: that is the cost of
        // adding one to the whitelist, and the reason the whitelist stays short.
        builder.HasIndex(list => new { list.OwnerId, list.Name, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_Name_Id");

        builder.HasIndex(list => new { list.OwnerId, list.CreatedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_CreatedAt_Id");

        builder.HasIndex(list => new { list.OwnerId, list.LastModifiedAt, list.Id })
            .HasDatabaseName("IX_TodoLists_OwnerId_LastModifiedAt_Id");

        builder.Property(list => list.Name)
            .HasMaxLength(TodoListName.MaxLength)
            .IsRequired();

        // PostgreSQL's xmin system column. It already exists on every table, so nothing
        // is created here; the mapping just tells EF to read it back after a write and to
        // include it in the WHERE clause of the next one. The token lives on the root only,
        // because the root is the consistency boundary — a concurrent edit to any item is a
        // conflict on the list.
        builder.Property(list => list.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(list => list.CreatedAt).IsRequired();
        builder.Property(list => list.CreatedBy);
        builder.Property(list => list.LastModifiedAt);
        builder.Property(list => list.LastModifiedBy);

        // Cascade, deliberately: an item has no meaning without its list, so a list
        // deleted while its items survive would leave rows no code path can ever reach.
        // The alternative — restricting the delete — would make deleting a list a
        // two-step operation the caller has to get right.
        builder.HasMany(list => list.Items)
            .WithOne()
            .HasForeignKey(item => item.TodoListId)
            .OnDelete(DeleteBehavior.Cascade);

    }
}

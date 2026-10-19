using AppTemplate.Application.Features.Notes.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Configurations;

/// <summary>
/// Mapping for the note row: column lengths, the indexes the sortable fields need and the
/// concurrency token. Lengths come from <see cref="NoteInputPolicy"/>, so the column and the
/// validator cannot drift apart.
/// </summary>
internal sealed class NoteRecordConfiguration : IEntityTypeConfiguration<NoteRecord>
{
    public void Configure(EntityTypeBuilder<NoteRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Notes", AppDbContext.NotesSchema);

        builder.HasKey(note => note.Id);

        // Ids are UUIDv7, created by the service: sequential enough to keep index inserts local, and
        // known before the insert.
        builder.Property(note => note.Id).ValueGeneratedNever();

        builder.Property(note => note.OwnerId).IsRequired();

        // One index per sortable field, leading with OwnerId, which every read filters by, and
        // ending in Id, the tiebreaker NoteSortMap always appends.
        builder.HasIndex(note => new { note.OwnerId, note.Title, note.Id })
            .HasDatabaseName("IX_Notes_OwnerId_Title_Id");

        builder.HasIndex(note => new { note.OwnerId, note.CreatedAt, note.Id })
            .HasDatabaseName("IX_Notes_OwnerId_CreatedAt_Id");

        builder.Property(note => note.Title)
            .HasMaxLength(NoteInputPolicy.TitleMaxLength)
            .IsRequired();

        builder.Property(note => note.Body)
            .HasMaxLength(NoteInputPolicy.BodyMaxLength)
            .IsRequired();

        builder.Property(note => note.CreatedAt).IsRequired();
        builder.Property(note => note.LastModifiedAt);

        // PostgreSQL's xmin system column, which already exists: EF reads it back after a write and
        // puts it in the WHERE clause of the next UPDATE or DELETE of the row.
        builder.Property(note => note.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}

using AppTemplate.Application.Features.Notes.Model.Policies;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Configurations;

internal sealed class NoteRecordConfiguration : IEntityTypeConfiguration<NoteRecord>
{
    public void Configure(EntityTypeBuilder<NoteRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Notes", AppDbContext.NotesSchema);

        builder.HasKey(note => note.Id);

        builder.Property(note => note.Id).ValueGeneratedNever();

        builder.Property(note => note.OwnerId).IsRequired();

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

        builder.Property(note => note.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}

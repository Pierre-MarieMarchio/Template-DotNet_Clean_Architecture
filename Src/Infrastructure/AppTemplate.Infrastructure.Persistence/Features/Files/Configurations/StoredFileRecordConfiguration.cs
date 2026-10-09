using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;

/// <summary>
/// Mapping for the stored-file row. String lengths come from the domain constants, and each of the
/// seven indexes serves a named caller: nothing is indexed speculatively.
/// </summary>
internal sealed class StoredFileRecordConfiguration : IEntityTypeConfiguration<StoredFileRecord>
{
    public void Configure(EntityTypeBuilder<StoredFileRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StoredFiles", AppDbContext.FilesSchema);

        builder.HasKey(file => file.Id);

        // Ids are UUIDv7, created by the domain.
        builder.Property(file => file.Id).ValueGeneratedNever();

        builder.Property(file => file.OwnerId).IsRequired();

        builder.Property(file => file.Category)
            .HasMaxLength(FileCategory.MaxLength)
            .IsRequired();

        builder.Property(file => file.ObjectKey)
            .HasMaxLength(ObjectKey.MaxLength)
            .IsRequired();

        builder.Property(file => file.Name)
            .HasMaxLength(StoredFileName.MaxLength)
            .IsRequired();

        builder.Property(file => file.DeclaredMediaType)
            .HasMaxLength(DeclaredMediaType.MaxLength)
            .IsRequired();

        builder.Property(file => file.SizeInBytes).IsRequired();

        // Not IsFixedLength: character(n) pads and ignores trailing spaces, unlike Sha256Checksum's ordinal equality.
        builder.Property(file => file.Checksum)
            .HasMaxLength(Sha256Checksum.Length)
            .IsRequired();

        builder.Property(file => file.State).IsRequired();
        builder.Property(file => file.RegisteredAt).IsRequired();
        builder.Property(file => file.AvailableAt);
        builder.Property(file => file.InspectionAttempts).IsRequired();
        builder.Property(file => file.NextInspectionAt);

        // Unique for safety: two rows sharing a key would have either deletion reclaim both rows' bytes.
        builder.HasIndex(file => file.ObjectKey)
            .IsUnique()
            .HasDatabaseName("IX_StoredFiles_ObjectKey");

        // One per field StoredFileCollectionPolicy whitelists: OwnerId first, Id last (the tiebreaker
        // StoredFileSortMap appends), so ORDER BY and the keyset comparison stay index-ordered.
        builder.HasIndex(file => new { file.OwnerId, file.Name, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_Name_Id");

        builder.HasIndex(file => new { file.OwnerId, file.RegisteredAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_RegisteredAt_Id");

        builder.HasIndex(file => new { file.OwnerId, file.AvailableAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_AvailableAt_Id");

        // The abandonment purge: "State = Pending AND RegisteredAt < cutoff", ordered by RegisteredAt.
        builder.HasIndex(file => new { file.State, file.RegisteredAt })
            .HasDatabaseName("IX_StoredFiles_State_RegisteredAt");

        // The inspection queue, partial so it holds only deposited rows.
        builder.HasIndex(file => new { file.NextInspectionAt, file.RegisteredAt, file.Id })
            .HasFilter($"\"State\" = {(int)StoredFileState.Deposited}")
            .HasDatabaseName("IX_StoredFiles_Deposited_NextInspectionAt_RegisteredAt_Id");

        // The quota read; SizeInBytes is included so the sums come from the index alone.
        builder.HasIndex(file => new { file.OwnerId, file.Category, file.State })
            .IncludeProperties(file => file.SizeInBytes)
            .HasDatabaseName("IX_StoredFiles_OwnerId_Category_State");

        // PostgreSQL's xmin system column, read back after a write and used in the next one's WHERE.
        builder.Property(file => file.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(file => file.CreatedAt).IsRequired();
        builder.Property(file => file.CreatedBy);
        builder.Property(file => file.LastModifiedAt);
        builder.Property(file => file.LastModifiedBy);

        builder.HasMany(file => file.Tags)
            .WithOne()
            .HasForeignKey(tag => tag.StoredFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

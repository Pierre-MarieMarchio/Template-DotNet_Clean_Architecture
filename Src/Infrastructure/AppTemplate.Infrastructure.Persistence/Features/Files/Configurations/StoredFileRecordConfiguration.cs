using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;

/// <summary>
/// Mapping for the stored-file row. Every string length is read from the domain's own constant, so a
/// column and the invariant behind it cannot drift apart.
/// <para>
/// Five indexes, and each one is here for a caller that can be named. Nothing is indexed
/// speculatively: an index nobody queries through is a write cost and a page of storage bought
/// against a guess.
/// </para>
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

        // Not IsFixedLength: character(n) pads with spaces and ignores trailing ones when comparing,
        // which would disagree with Sha256Checksum's ordinal equality.
        builder.Property(file => file.Checksum)
            .HasMaxLength(Sha256Checksum.Length)
            .IsRequired();

        builder.Property(file => file.State).IsRequired();
        builder.Property(file => file.RegisteredAt).IsRequired();
        builder.Property(file => file.AvailableAt);

        // Unique as a safety property, not as an optimisation: bytes are reclaimed by deleting every
        // object no row names, so two rows sharing a key would have either deletion reclaim both.
        builder.HasIndex(file => file.ObjectKey)
            .IsUnique()
            .HasDatabaseName("IX_StoredFiles_ObjectKey");

        // One index per field StoredFileCollectionPolicy whitelists, leading with OwnerId, which
        // every read filters by, and ending in Id, the tiebreaker StoredFileSortMap always appends:
        // the ORDER BY and the keyset comparison that resumes it both stay index-ordered.
        builder.HasIndex(file => new { file.OwnerId, file.Name, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_Name_Id");

        builder.HasIndex(file => new { file.OwnerId, file.RegisteredAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_RegisteredAt_Id");

        builder.HasIndex(file => new { file.OwnerId, file.AvailableAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_AvailableAt_Id");

        // "State = x AND RegisteredAt < cutoff", ordered by RegisteredAt: State leads as the
        // equality filter, RegisteredAt trails as both the range predicate and the sort key. Both
        // sweeps read this shape, which is why the inspection pass orders by registration too.
        builder.HasIndex(file => new { file.State, file.RegisteredAt })
            .HasDatabaseName("IX_StoredFiles_State_RegisteredAt");

        // PostgreSQL's xmin system column, which already exists: EF reads it back after a write and
        // puts it in the WHERE clause of the next one.
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

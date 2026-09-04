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

        // Ids are UUIDv7, created by the domain, so the database must not try to generate one.
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

        // Not IsFixedLength: PostgreSQL's character(n) pads with spaces and ignores trailing ones when
        // comparing, so a digest would be one value to the database and another to Sha256Checksum's
        // ordinal equality. varchar(64) stores exactly what was written.
        builder.Property(file => file.Checksum)
            .HasMaxLength(Sha256Checksum.Length)
            .IsRequired();

        builder.Property(file => file.State).IsRequired();
        builder.Property(file => file.RegisteredAt).IsRequired();
        builder.Property(file => file.AvailableAt);

        // Unique, and this one is a safety property rather than a lookup optimisation. The bytes of a
        // file are reclaimed by deleting every object no row names, so two rows sharing a key would
        // make deleting either of them reclaim the content of both — the survivor would keep a row
        // pointing at bytes that are gone. The database refusing the second row is what makes that
        // state unreachable. It also serves IStoredFileRepository.GetByObjectKeyAsync, which is how a
        // report arriving from the object store itself names a file, and IStoredFileQueries's
        // GetLiveObjectKeysAsync, whose page of candidate keys is probed straight through it.
        builder.HasIndex(file => file.ObjectKey)
            .IsUnique()
            .HasDatabaseName("IX_StoredFiles_ObjectKey");

        // One composite index per field StoredFileCollectionPolicy whitelists, each leading with
        // OwnerId — every read of the list filters by it — and ending in Id, the tiebreaker
        // StoredFileSortMap always appends, so both the ORDER BY and the keyset comparison that
        // resumes it stay index-ordered. Caller: GetStoredFilesUseCase, one index per sort= value it
        // accepts. Leading with OwnerId is also what lets GetUsageForOwnerAsync read one owner's
        // totals without touching anybody else's rows.
        builder.HasIndex(file => new { file.OwnerId, file.Name, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_Name_Id");

        builder.HasIndex(file => new { file.OwnerId, file.RegisteredAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_RegisteredAt_Id");

        builder.HasIndex(file => new { file.OwnerId, file.AvailableAt, file.Id })
            .HasDatabaseName("IX_StoredFiles_OwnerId_AvailableAt_Id");

        // Serves the abandonment sweep's "State = Pending AND RegisteredAt < cutoff" scan, ordered by
        // RegisteredAt: State leads because it is an equality filter, RegisteredAt trails because it is
        // both the range predicate and the sort key. Caller:
        // PurgeAbandonedRegistrationsUseCase, through IStoredFileRepository.GetPendingRegisteredBeforeAsync.
        // Without it, every pass is a full table scan over files that are overwhelmingly Available.
        // It serves the inspection pass on the same shape — "State = Deposited", ordered by
        // RegisteredAt, through IStoredFileRepository.GetDepositedAsync — which is why that query
        // orders by registration rather than by anything closer to when the deposit landed: a second
        // ordering would have bought a second index for a set that is normally almost empty.
        builder.HasIndex(file => new { file.State, file.RegisteredAt })
            .HasDatabaseName("IX_StoredFiles_State_RegisteredAt");

        // PostgreSQL's xmin system column. Nothing is created here; the mapping just tells EF to read it
        // back after a write and to include it in the WHERE clause of the next one.
        builder.Property(file => file.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Property(file => file.CreatedAt).IsRequired();
        builder.Property(file => file.CreatedBy);
        builder.Property(file => file.LastModifiedAt);
        builder.Property(file => file.LastModifiedBy);
    }
}

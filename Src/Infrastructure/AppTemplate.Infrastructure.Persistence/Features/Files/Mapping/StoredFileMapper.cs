using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;

/// <summary>
/// The one place that knows both shapes. Stateless, so it is a singleton and testable without a
/// database. Read <see cref="IStoredFileMapper"/> before touching <c>ObjectKey</c>.
/// </summary>
internal sealed class StoredFileMapper : IStoredFileMapper
{
    public StoredFile ToAggregate(StoredFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Rebuilt through each value object's factory, so a row that breaks a tightened rule is refused.
        var aggregate = StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = record.Id,
            OwnerId = UserId.Create(record.OwnerId),
            Category = FileCategory.Create(record.Category),
            ObjectKey = ObjectKey.Create(record.ObjectKey),
            Name = StoredFileName.Create(record.Name),
            DeclaredMediaType = DeclaredMediaType.Create(record.DeclaredMediaType),
            Size = FileSize.Create(record.SizeInBytes),
            Checksum = Sha256Checksum.Create(record.Checksum),
            State = record.State,
            RegisteredAt = record.RegisteredAt,
            AvailableAt = record.AvailableAt,
            InspectionAttempts = record.InspectionAttempts,
            NextInspectionAt = record.NextInspectionAt,
            Tags = record.Tags.Select(tag => tag.Value),
        });

        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "Stored file");

        return aggregate;
    }

    public StoredFileRecord ToNewRecord(StoredFile aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        var record = new StoredFileRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId.Value,
            Category = aggregate.Category.Value,

            // Verbatim, never normalised: a key this row does not match belongs to nobody for the sweep.
            ObjectKey = aggregate.ObjectKey.Value,
            Name = aggregate.Name.Value,
            DeclaredMediaType = aggregate.DeclaredMediaType.Value,
            SizeInBytes = aggregate.Size.Bytes,
            Checksum = aggregate.Checksum.Value,
            State = aggregate.State,
            RegisteredAt = aggregate.RegisteredAt,
            AvailableAt = aggregate.AvailableAt,
            InspectionAttempts = aggregate.InspectionAttempts,
            NextInspectionAt = aggregate.NextInspectionAt,

            // Overwritten on insert by xmin; carried so the round trip stays total.
            Version = aggregate.Version,

            // Overwritten by the audit interceptor, which runs after this.
            CreatedAt = aggregate.CreatedAt,
            CreatedBy = aggregate.CreatedBy,
            LastModifiedAt = aggregate.LastModifiedAt,
            LastModifiedBy = aggregate.LastModifiedBy,
        };

        ReconcileTags(aggregate, record);

        return record;
    }

    /// <summary>Reconciles the tag rows by hand, inserting and deleting.</summary>
    private static void ReconcileTags(StoredFile aggregate, StoredFileRecord record)
    {
        var unmatched = record.Tags.ToDictionary(tag => tag.Value, StringComparer.Ordinal);

        foreach (var tag in aggregate.Tags)
        {
            if (!unmatched.Remove(tag.Value))
            {
                record.Tags.Add(new StoredFileTagRecord { StoredFileId = aggregate.Id, Value = tag.Value });
            }
        }

        foreach (var orphan in unmatched.Values)
        {
            record.Tags.Remove(orphan);
        }
    }

    public void WriteTo(StoredFile aggregate, StoredFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        // Assigned, not replaced: EF writes a column only if the value differs from the one it read.
        record.OwnerId = aggregate.OwnerId.Value;
        record.Category = aggregate.Category.Value;

        record.ObjectKey = aggregate.ObjectKey.Value;
        record.Name = aggregate.Name.Value;
        record.DeclaredMediaType = aggregate.DeclaredMediaType.Value;
        record.SizeInBytes = aggregate.Size.Bytes;
        record.Checksum = aggregate.Checksum.Value;
        record.State = aggregate.State;
        record.RegisteredAt = aggregate.RegisteredAt;
        record.AvailableAt = aggregate.AvailableAt;
        record.InspectionAttempts = aggregate.InspectionAttempts;
        record.NextInspectionAt = aggregate.NextInspectionAt;

        ReconcileTags(aggregate, record);

        // Version and audit stamps are not written here: PostgreSQL and the interceptor own them.
    }
}

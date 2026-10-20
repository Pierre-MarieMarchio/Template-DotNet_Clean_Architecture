using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;

internal sealed class StoredFileMapper : IStoredFileMapper
{
    public StoredFile ToAggregate(StoredFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

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

            Version = aggregate.Version,

            CreatedAt = aggregate.CreatedAt,
            CreatedBy = aggregate.CreatedBy,
            LastModifiedAt = aggregate.LastModifiedAt,
            LastModifiedBy = aggregate.LastModifiedBy,
        };

        ReconcileTags(aggregate, record);

        return record;
    }

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
    }
}

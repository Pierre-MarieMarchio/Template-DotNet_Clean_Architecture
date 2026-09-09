using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;

/// <summary>
/// The one place that knows both shapes.
/// <para>
/// Stateless and registered as a singleton: it touches no <c>DbContext</c>, so it can be shared, and the
/// fidelity tests can exercise it with no database at all.
/// </para>
/// <para>
/// <b>Read <see cref="IStoredFileMapper"/> before editing anything about <c>ObjectKey</c> below.</b> It
/// is the one value here whose corruption is not a lost field but a deleted file.
/// </para>
/// </summary>
internal sealed class StoredFileMapper : IStoredFileMapper
{
    public StoredFile ToAggregate(StoredFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Rebuilt through each value object's factory, so a row that predates a tightened rule is
        // refused on the way in. ObjectKey.Create is looser than ObjectKey.New for that reason.
        var aggregate = StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = record.Id,
            OwnerId = UserId.Create(record.OwnerId),
            ObjectKey = ObjectKey.Create(record.ObjectKey),
            Name = StoredFileName.Create(record.Name),
            DeclaredMediaType = DeclaredMediaType.Create(record.DeclaredMediaType),
            Size = FileSize.Create(record.SizeInBytes),
            Checksum = Sha256Checksum.Create(record.Checksum),
            State = record.State,
            RegisteredAt = record.RegisteredAt,
            AvailableAt = record.AvailableAt,
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

            // Verbatim, never normalised: the store resolves keys literally, and a key this row does
            // not match is one the orphan sweep treats as belonging to nobody.
            ObjectKey = aggregate.ObjectKey.Value,
            Name = aggregate.Name.Value,
            DeclaredMediaType = aggregate.DeclaredMediaType.Value,
            SizeInBytes = aggregate.Size.Bytes,
            Checksum = aggregate.Checksum.Value,
            State = aggregate.State,
            RegisteredAt = aggregate.RegisteredAt,
            AvailableAt = aggregate.AvailableAt,

            // Overwritten on insert, where PostgreSQL assigns xmin. Carried so the round trip stays
            // total and the fidelity test can check it.
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

    /// <summary>
    /// The tag rows this file should end up with. By hand, and in both directions: a tag the
    /// aggregate holds and the row does not is inserted, one the row holds and the aggregate does
    /// not is deleted.
    /// </summary>
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

        // Written on every flush although no operation moves it, so the column is asserted rather
        // than assumed.
        record.ObjectKey = aggregate.ObjectKey.Value;
        record.Name = aggregate.Name.Value;
        record.DeclaredMediaType = aggregate.DeclaredMediaType.Value;
        record.SizeInBytes = aggregate.Size.Bytes;
        record.Checksum = aggregate.Checksum.Value;
        record.State = aggregate.State;
        record.RegisteredAt = aggregate.RegisteredAt;
        record.AvailableAt = aggregate.AvailableAt;

        ReconcileTags(aggregate, record);

        // Version and the audit stamps are not written here: the token is PostgreSQL's, the stamps
        // are the interceptor's, and a second writer for either would eventually disagree.
    }
}

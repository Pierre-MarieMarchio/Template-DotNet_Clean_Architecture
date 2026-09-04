using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
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

        // Every value object is rebuilt through its own factory rather than assigned, so a row that
        // predates a tightened rule is refused on the way in instead of becoming an aggregate that
        // breaks it. ObjectKey.Create is deliberately looser than ObjectKey.New for exactly this
        // reason — see the value object.
        var aggregate = StoredFile.Rehydrate(
            record.Id,
            record.OwnerId,
            ObjectKey.Create(record.ObjectKey),
            StoredFileName.Create(record.Name),
            DeclaredMediaType.Create(record.DeclaredMediaType),
            FileSize.Create(record.SizeInBytes),
            Sha256Checksum.Create(record.Checksum),
            record.State,
            record.RegisteredAt,
            record.AvailableAt);

        // The version and the audit stamps are read back through StoredStamps, not assigned here: the
        // aggregate exposes them as read-only properties, settable only through the explicit interfaces
        // that mark this as the persistence layer.
        StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "Stored file");

        return aggregate;
    }

    public StoredFileRecord ToNewRecord(StoredFile aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return new StoredFileRecord
        {
            Id = aggregate.Id,
            OwnerId = aggregate.OwnerId,

            // The key the upload grant was minted against. Verbatim: nothing here may normalise it,
            // because the store resolves keys literally and a key this row does not match is a key
            // the orphan sweep treats as belonging to nobody.
            ObjectKey = aggregate.ObjectKey.Value,
            Name = aggregate.Name.Value,
            DeclaredMediaType = aggregate.DeclaredMediaType.Value,
            SizeInBytes = aggregate.Size.Bytes,
            Checksum = aggregate.Checksum.Value,
            State = aggregate.State,
            RegisteredAt = aggregate.RegisteredAt,
            AvailableAt = aggregate.AvailableAt,

            // Carried even though the store owns it. On an insert PostgreSQL assigns xmin itself and EF
            // ignores whatever is here, but writing it keeps this method total — and a total method is
            // what the round-trip fidelity test can check.
            Version = aggregate.Version,

            // Likewise carried, and likewise overwritten: the audit interceptor stamps every Added entry
            // after this runs.
            CreatedAt = aggregate.CreatedAt,
            CreatedBy = aggregate.CreatedBy,
            LastModifiedAt = aggregate.LastModifiedAt,
            LastModifiedBy = aggregate.LastModifiedBy,
        };
    }

    public void WriteTo(StoredFile aggregate, StoredFileRecord record)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(record);

        // Assigned, not replaced. EF compares each value against the one it read and writes a column
        // only if it actually differs, so an unchanged aggregate produces no UPDATE at all.
        record.OwnerId = aggregate.OwnerId;

        // Written on every flush although no operation can move it, and that is the point: the column
        // is asserted to still hold the key the bytes are under rather than left alone and assumed to.
        record.ObjectKey = aggregate.ObjectKey.Value;
        record.Name = aggregate.Name.Value;
        record.DeclaredMediaType = aggregate.DeclaredMediaType.Value;
        record.SizeInBytes = aggregate.Size.Bytes;
        record.Checksum = aggregate.Checksum.Value;
        record.State = aggregate.State;
        record.RegisteredAt = aggregate.RegisteredAt;
        record.AvailableAt = aggregate.AvailableAt;

        // Version, CreatedAt, CreatedBy, LastModifiedAt and LastModifiedBy are deliberately NOT written
        // here. The concurrency token belongs to PostgreSQL and the audit stamps belong to the
        // interceptor; the aggregate received both on load and receives them again after each save. A
        // second writer for either would be a second opinion, and the two would eventually differ.
    }
}

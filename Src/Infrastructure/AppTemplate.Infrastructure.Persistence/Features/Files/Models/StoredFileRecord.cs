using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Models;

/// <summary>
/// How a stored file is stored: a row, with settable properties, no behaviour and no invariants. The
/// aggregate that owns the rules is <see cref="Domain.Features.Files.Entities.StoredFile"/>; this type
/// answers only to the schema.
/// <para>
/// <see cref="StoredFileState"/> is reused as-is rather than given a persistence-side twin, for the same
/// reason <c>ReminderRecord</c> reuses its own: it is a plain enumeration with no method and no rule
/// attached, so it imposes nothing on how the domain expresses itself. It is stored as its integer
/// value, so its members may be reordered and may not be renumbered — the enum says so where the
/// numbers are. There is no member meaning "deleted" and no column for one: a deleted file is a row
/// that is gone. Nor is there a column saying why a file was quarantined, and that too is a decision
/// rather than an omission — see <see cref="StoredFileState.Quarantined"/>.
/// </para>
/// <para>
/// <b><see cref="ObjectKey"/> is the one column here that names something this database cannot see.</b>
/// Every other value describes the file; that one addresses the bytes, in a store whose only record of
/// which objects are owed is this column. Writing a different value into it does not corrupt a row, it
/// unlinks a live file's content from the only thing that vouches for it, and the orphan sweep then
/// deletes the bytes. <c>StoredFileMapper</c> is where that can happen, and its documentation says so.
/// </para>
/// </summary>
internal sealed class StoredFileRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>The key exactly as it was minted. Unique across the table — see the configuration.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DeclaredMediaType { get; set; } = string.Empty;

    /// <summary>
    /// Named for its unit rather than after the aggregate's <c>Size</c>, because a column holding a
    /// bare number has no value object to say what the number counts.
    /// </summary>
    public long SizeInBytes { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public StoredFileState State { get; set; } = StoredFileState.Pending;

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset? AvailableAt { get; set; }

    /// <summary>
    /// PostgreSQL's <c>xmin</c> system column. Never written by this process: the database moves it on
    /// every <c>UPDATE</c>, EF reads it back, and the value goes into the <c>WHERE</c> clause of the
    /// next write.
    /// </summary>
    public uint Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public Guid? LastModifiedBy { get; set; }

    void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
    {
        LastModifiedAt = at;
        LastModifiedBy = by;
    }
}

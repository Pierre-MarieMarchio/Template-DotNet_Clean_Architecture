using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Models;

/// <summary>
/// How a stored file is stored: a row with no behaviour and no invariants; the rules are
/// <see cref="Domain.Features.Files.Model.Entities.StoredFile"/>'s.
/// <para>
/// <see cref="StoredFileState"/> is reused as its integer value: members may be reordered, never
/// renumbered. No column means "deleted" or says why a file was quarantined, deliberately.
/// </para>
/// <para>
/// <see cref="ObjectKey"/> addresses bytes this database cannot see, and it is the only record of
/// which objects are owed: writing a different value unlinks a live file's content and the orphan
/// sweep then deletes it. <c>StoredFileMapper</c> is where that could happen.
/// </para>
/// </summary>
internal sealed class StoredFileRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>The category's slug, as registered.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>The key exactly as it was minted. Unique across the table — see the configuration.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DeclaredMediaType { get; set; } = string.Empty;

    /// <summary>Named for its unit, since a bare column has no value object to say what it counts.</summary>
    public long SizeInBytes { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public StoredFileState State { get; set; } = StoredFileState.Pending;

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset? AvailableAt { get; set; }

    public int InspectionAttempts { get; set; }

    public DateTimeOffset? NextInspectionAt { get; set; }

    /// <summary>The tag rows, reconciled by hand — see <see cref="StoredFileTagRecord"/>.</summary>
    public ICollection<StoredFileTagRecord> Tags { get; } = [];

    /// <summary>PostgreSQL's <c>xmin</c>: moved by the database on every <c>UPDATE</c>, never written here, used in the next write's <c>WHERE</c>.</summary>
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

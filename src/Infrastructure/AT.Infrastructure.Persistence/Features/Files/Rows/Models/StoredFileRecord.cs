using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

internal sealed class StoredFileRecord : IAuditable
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string ObjectKey { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string DeclaredMediaType { get; set; } = string.Empty;

    public long SizeInBytes { get; set; }

    public string Checksum { get; set; } = string.Empty;

    public StoredFileState State { get; set; } = StoredFileState.Pending;

    public DateTimeOffset RegisteredAt { get; set; }

    public DateTimeOffset? AvailableAt { get; set; }

    public int InspectionAttempts { get; set; }

    public DateTimeOffset? NextInspectionAt { get; set; }

    public ICollection<StoredFileTagRecord> Tags { get; } = [];

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

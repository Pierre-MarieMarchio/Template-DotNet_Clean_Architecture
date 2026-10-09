namespace AppTemplate.Infrastructure.Persistence.Features.Files.Models;

/// <summary>
/// One tag on one file; the key <c>(StoredFileId, Value)</c> makes a duplicate unrepresentable. An
/// ordinary entity rather than an owned collection, because the mapper reconciles it by hand.
/// </summary>
internal sealed class StoredFileTagRecord
{
    public Guid StoredFileId { get; set; }

    public string Value { get; set; } = string.Empty;
}

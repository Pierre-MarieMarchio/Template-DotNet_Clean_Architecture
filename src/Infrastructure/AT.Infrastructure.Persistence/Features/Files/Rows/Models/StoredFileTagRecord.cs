namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

internal sealed class StoredFileTagRecord
{
    public Guid StoredFileId { get; set; }

    public string Value { get; set; } = string.Empty;
}

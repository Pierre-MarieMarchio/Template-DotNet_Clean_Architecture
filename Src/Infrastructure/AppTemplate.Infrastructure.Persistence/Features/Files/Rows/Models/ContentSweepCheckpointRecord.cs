namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

internal sealed class ContentSweepCheckpointRecord
{
    public string Name { get; set; } = string.Empty;

    public string? LastKey { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

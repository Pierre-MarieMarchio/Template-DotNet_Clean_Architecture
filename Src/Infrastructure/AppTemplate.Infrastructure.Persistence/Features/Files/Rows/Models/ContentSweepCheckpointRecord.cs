namespace AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;

/// <summary>
/// Where a walk of the object store has reached, one row per named walk. Written only by
/// <c>OrphanSweepCheckpoint</c>.
/// </summary>
internal sealed class ContentSweepCheckpointRecord
{
    /// <summary>The walk this row belongs to.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The last key a finished page ended on, or <c>null</c> to start from the beginning.</summary>
    public string? LastKey { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

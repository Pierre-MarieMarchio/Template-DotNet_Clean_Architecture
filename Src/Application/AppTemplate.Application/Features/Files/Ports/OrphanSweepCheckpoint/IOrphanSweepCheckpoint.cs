namespace AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;

/// <summary>
/// Where the orphan sweep's walk of the key namespace has reached, kept across passes and restarts.
/// </summary>
public interface IOrphanSweepCheckpoint
{
    /// <summary>The last key a finished page ended on, or null to start from the beginning.</summary>
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="lastKey"/> at once, in a write no business transaction shares; null restarts the
    /// walk.
    /// </summary>
    Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default);
}

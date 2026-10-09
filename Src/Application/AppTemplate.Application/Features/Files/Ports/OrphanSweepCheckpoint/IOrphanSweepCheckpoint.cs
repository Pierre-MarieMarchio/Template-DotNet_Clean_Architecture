namespace AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;

/// <summary>
/// Where the orphan sweep's walk of the key namespace has reached, kept across passes and restarts so
/// that a pass resumes after the last page the previous one finished.
/// </summary>
public interface IOrphanSweepCheckpoint
{
    /// <returns>The last key a finished page ended on, or <c>null</c> to start from the beginning.</returns>
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="lastKey"/> at once, in a write of its own that no business
    /// transaction shares; <c>null</c> restarts the next walk from the beginning.
    /// </summary>
    Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default);
}

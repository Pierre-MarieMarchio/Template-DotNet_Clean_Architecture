namespace AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;

public interface IOrphanSweepCheckpoint
{
    Task<string?> GetAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default);
}

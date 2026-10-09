using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IOrphanSweepCheckpoint"/> held in this process, for a host without PostgreSQL. It
/// survives passes but not a restart, after which the walk starts from the beginning again.
/// </summary>
internal sealed class InMemoryOrphanSweepCheckpoint : IOrphanSweepCheckpoint
{
    private string? _lastKey;

    public Task<string?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Volatile.Read(ref _lastKey));

    public Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default)
    {
        Volatile.Write(ref _lastKey, lastKey);

        return Task.CompletedTask;
    }
}

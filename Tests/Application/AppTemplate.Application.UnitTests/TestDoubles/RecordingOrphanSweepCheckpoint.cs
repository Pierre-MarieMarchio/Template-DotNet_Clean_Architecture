using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>A checkpoint that outlives the use case reading it, and records every save in order.</summary>
internal sealed class RecordingOrphanSweepCheckpoint : IOrphanSweepCheckpoint
{
    public string? LastKey { get; set; }

    public List<string?> Saves { get; } = [];

    public Task<string?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(LastKey);

    public Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default)
    {
        LastKey = lastKey;
        Saves.Add(lastKey);

        return Task.CompletedTask;
    }
}

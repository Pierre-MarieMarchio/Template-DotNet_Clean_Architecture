using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Checkpoints;

/// <summary>
/// <see cref="IOrphanSweepCheckpoint"/> as one row of <c>files.ContentSweepCheckpoints</c>. Each call
/// opens its own context, so the checkpoint commits at once, outside the scope's unit of work.
/// </summary>
internal sealed class OrphanSweepCheckpoint(
    IDbContextFactory<AppDbContext> contextFactory,
    IDateTimeProvider dateTimeProvider) : IOrphanSweepCheckpoint
{
    /// <summary>The row's key. Stored data: renaming it restarts the walk from the beginning.</summary>
    internal const string WalkName = "reclaim-orphans";

    public async Task<string?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.ContentSweepCheckpoints
            .AsNoTracking()
            .Where(checkpoint => checkpoint.Name == WalkName)
            .Select(checkpoint => checkpoint.LastKey)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(string? lastKey, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = dateTimeProvider.UtcNow;

        if (await UpdateAsync(context, lastKey, now, cancellationToken) > 0)
        {
            return;
        }

        var entry = context.ContentSweepCheckpoints.Add(new ContentSweepCheckpointRecord
        {
            Name = WalkName,
            LastKey = lastKey,
            UpdatedAt = now,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another writer inserted the row first; this value is the later one, so it wins.
            entry.State = EntityState.Detached;
            await UpdateAsync(context, lastKey, now, cancellationToken);
        }
    }

    private static Task<int> UpdateAsync(
        AppDbContext context,
        string? lastKey,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.ContentSweepCheckpoints
            .Where(checkpoint => checkpoint.Name == WalkName)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(checkpoint => checkpoint.LastKey, lastKey)
                    .SetProperty(checkpoint => checkpoint.UpdatedAt, now),
                cancellationToken);
}

using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Repositories;

/// <summary>
/// Loads and stages <see cref="StoredFile"/> aggregates on a borrowed context; committing belongs to
/// <c>IUnitOfWork</c>.
/// </summary>
internal sealed class StoredFileRepository(
    AppDbContext context,
    IStoredFileMapper mapper,
    IStoredFileTracker tracker) : IStoredFileRepository
{
    public async Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // Identity map first: two copies of one aggregate would let the flush keep whichever it saw last.
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        // Tags are included on every path: without them the file looks untagged and the next write re-inserts them.
        var record = await context.StoredFiles
            .Include(file => file.Tags)
            .FirstOrDefaultAsync(file => file.Id == id, cancellationToken);

        return record is null ? null : LoadOrTrack(record);
    }

    public async Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Oldest first, so a backlog larger than one batch does not re-serve the same rows every pass.
        var records = await context.StoredFiles
            .Include(file => file.Tags)
            .Where(file => file.State == StoredFileState.Pending && file.RegisteredAt < registeredBefore)
            .OrderBy(file => file.RegisteredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public async Task<IReadOnlyList<StoredFile>> GetDepositedAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var records = await context.StoredFiles
            .Include(file => file.Tags)
            .Where(file => file.State == StoredFileState.Deposited
                && (file.NextInspectionAt == null || file.NextInspectionAt <= asOf))
            .OrderBy(file => file.NextInspectionAt ?? file.RegisteredAt)
            .ThenBy(file => file.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public void Add(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        var record = mapper.ToNewRecord(storedFile);

        context.StoredFiles.Add(record);

        // Tracked, so a mutation after Add is mapped onto this row before the save.
        tracker.Track(storedFile, record);
    }

    public void Remove(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        // The fallback stub carries the key and version, so the delete is still checked against the caller's token.
        var record = tracker.FindRecord(storedFile.Id)
            ?? new StoredFileRecord { Id = storedFile.Id, Version = storedFile.Version };

        context.StoredFiles.Remove(record);
        tracker.MarkRemoved(storedFile, record);
    }

    /// <summary>The tracked aggregate for a row already known to this request, or a freshly mapped and tracked one.</summary>
    private StoredFile LoadOrTrack(StoredFileRecord record)
    {
        if (tracker.Find(record.Id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var aggregate = mapper.ToAggregate(record);
        tracker.Track(aggregate, record);

        return aggregate;
    }
}

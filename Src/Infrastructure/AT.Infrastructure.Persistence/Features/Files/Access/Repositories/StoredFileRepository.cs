using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Files.Access.Repositories;

internal sealed class StoredFileRepository(
    AppDbContext context,
    IStoredFileMapper mapper,
    IStoredFileTracker tracker) : IStoredFileRepository
{
    public async Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

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

        tracker.Track(storedFile, record);
    }

    public async Task<IReadOnlyList<StoredFile>> GetForOwnerAsync(
        UserId ownerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var records = await context.StoredFiles
            .Include(file => file.Tags)
            .Where(file => file.OwnerId == ownerId.Value)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public void Remove(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        var record = tracker.FindRecord(storedFile.Id)
            ?? new StoredFileRecord { Id = storedFile.Id, Version = storedFile.Version };

        context.StoredFiles.Remove(record);
        tracker.MarkRemoved(storedFile, record);
    }

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

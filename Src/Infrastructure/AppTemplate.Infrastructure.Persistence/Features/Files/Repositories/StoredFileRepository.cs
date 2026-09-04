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
/// Loads and stages <see cref="StoredFile"/> aggregates. Nothing here calls <c>SaveChangesAsync</c>: it
/// borrows the context and never owns the transaction. Committing belongs to <c>IUnitOfWork</c>.
/// <para>
/// Internal and sealed: it is an adapter for a port the domain layer declares, and nothing outside this
/// assembly has any business naming the type. Callers depend on <see cref="IStoredFileRepository"/>.
/// </para>
/// </summary>
internal sealed class StoredFileRepository(
    AppDbContext context,
    IStoredFileMapper mapper,
    IStoredFileTracker tracker) : IStoredFileRepository
{
    public async Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // The identity map first. Two use cases in one request asking for the same file must get the
        // same object, or each would decide against its own copy and the flush would keep whichever it
        // saw last.
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var record = await context.StoredFiles.FirstOrDefaultAsync(
            file => file.Id == id,
            cancellationToken);

        return record is null ? null : LoadOrTrack(record);
    }

    public async Task<StoredFile?> GetByObjectKeyAsync(ObjectKey objectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(objectKey);

        // The key is compared as the string it is stored as, ordinally, exactly as the object store
        // resolves it. The unique index on this column is what makes at most one row match.
        var record = await context.StoredFiles.FirstOrDefaultAsync(
            file => file.ObjectKey == objectKey.Value,
            cancellationToken);

        return record is null ? null : LoadOrTrack(record);
    }

    public async Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Strictly before, as the contract says. Oldest first, so a backlog larger than one batch is
        // worked through in registration order rather than a batch of the same rows every pass.
        var records = await context.StoredFiles
            .Where(file => file.State == StoredFileState.Pending && file.RegisteredAt < registeredBefore)
            .OrderBy(file => file.RegisteredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public async Task<IReadOnlyList<StoredFile>> GetDepositedAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Served by IX_StoredFiles_State_RegisteredAt, the same index the abandonment sweep reads:
        // State leads because it is an equality filter and RegisteredAt trails because it is the
        // sort key. No new index is owed for this query, which is the whole reason it orders by
        // RegisteredAt rather than by anything nearer to when the deposit arrived.
        var records = await context.StoredFiles
            .Where(file => file.State == StoredFileState.Deposited)
            .OrderBy(file => file.RegisteredAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public void Add(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        var record = mapper.ToNewRecord(storedFile);

        context.StoredFiles.Add(record);

        // Tracked like any other: the flush pipeline will map onto this row again before the save, which
        // is how a mutation made after Add still lands.
        tracker.Track(storedFile, record);
    }

    public void Remove(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        // Ordinarily the row is already tracked, because a delete follows a load. The fallback attaches
        // a stub carrying the key and the version, so a caller who reconstructed an aggregate elsewhere
        // still gets a delete rather than a silent no-op — and still gets it checked against the token
        // it decided on, because attaching snapshots the current values as the original ones.
        var record = tracker.FindRecord(storedFile.Id)
            ?? new StoredFileRecord { Id = storedFile.Id, Version = storedFile.Version };

        context.StoredFiles.Remove(record);
        tracker.MarkRemoved(storedFile, record);
    }

    /// <summary>
    /// Hands back the tracked aggregate for a row that is already known to this request, or maps and
    /// tracks a fresh one. Needed by every query that can return more than one row: unlike
    /// <see cref="GetAsync"/>, which only ever looks up one id, these can revisit a file already loaded
    /// earlier in the same request, and skipping the identity map would hand out a second, divergent
    /// copy of it.
    /// </summary>
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

using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// A stored-file store with the two properties of the real one a background pass depends on: a save
/// writes every changed aggregate or none, and a write against a version that has since moved is
/// refused and stays staged, as it does in the EF context. Repository and unit of work in one.
/// <see cref="GetDepositedAsync"/> follows the contract <see cref="IStoredFileRepository"/> documents.
/// </summary>
internal sealed class InMemoryStoredFileStore : IStoredFileRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Row> _committed = [];
    private readonly Dictionary<Guid, Loaded> _loaded = [];

    /// <summary>How many saves were attempted, successful or not.</summary>
    public int SaveAttempts { get; private set; }

    /// <summary>How many saves succeeded.</summary>
    public int Commits { get; private set; }

    /// <summary>Puts a file in the store as if it had been saved before.</summary>
    public void Seed(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        _committed[storedFile.Id] = new Row(Snapshot(storedFile), 1);
    }

    /// <summary>What a new job pass gets: nothing loaded, nothing staged.</summary>
    public void StartNewScope() => _loaded.Clear();

    /// <summary>The state the store holds for a file, as a fresh aggregate would load it.</summary>
    public StoredFile Committed(Guid id) => Load(_committed[id]);

    /// <summary>Another process changes the file and commits, moving its version under any earlier reader.</summary>
    public void ChangeBehindTheBack(Guid id)
    {
        var row = _committed[id];
        var changed = Load(row);
        changed.SetTags(["changed elsewhere"]);

        _committed[id] = new Row(Snapshot(changed), row.Version + 1);
    }

    public Task<IReadOnlyList<StoredFile>> GetDepositedAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var deposited = _committed.Values
            .Where(row => row.Snapshot.State == StoredFileState.Deposited
                && (row.Snapshot.NextInspectionAt is null || row.Snapshot.NextInspectionAt <= asOf))
            .OrderBy(row => row.Snapshot.NextInspectionAt ?? row.Snapshot.RegisteredAt)
            .ThenBy(row => row.Snapshot.Id)
            .Take(batchSize)
            .Select(Track)
            .ToList();

        return Task.FromResult<IReadOnlyList<StoredFile>>(deposited);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveAttempts++;

        var changed = _loaded.Values
            .Where(loaded => Key(Snapshot(loaded.Aggregate)) != Key(_committed[loaded.Aggregate.Id].Snapshot))
            .ToList();

        if (changed.Any(loaded => loaded.Version != _committed[loaded.Aggregate.Id].Version))
        {
            throw new ConcurrencyConflictException("A stored file was changed by somebody else.");
        }

        foreach (var loaded in changed)
        {
            uint version = loaded.Version + 1;

            _committed[loaded.Aggregate.Id] = new Row(Snapshot(loaded.Aggregate), version);
            ((IVersioned)loaded.Aggregate).SetVersion(version);
            loaded.Version = version;
        }

        Commits++;

        return Task.FromResult(changed.Count);
    }

    public Task<StoredFile?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<StoredFile?>(_committed.TryGetValue(id, out var row) ? Track(row) : null);

    public Task<IReadOnlyList<StoredFile>> GetPendingRegisteredBeforeAsync(
        DateTimeOffset registeredBefore,
        int batchSize,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No inspection test reads pending registrations.");

    public void Add(StoredFile storedFile) => throw new NotSupportedException("No background-pass test registers a file.");

    public void Remove(StoredFile storedFile) => throw new NotSupportedException("No inspection test removes a file.");

    private StoredFile Track(Row row)
    {
        if (_loaded.TryGetValue(row.Snapshot.Id, out var existing))
        {
            return existing.Aggregate;
        }

        var aggregate = Load(row);
        _loaded[row.Snapshot.Id] = new Loaded(aggregate, row.Version);

        return aggregate;
    }

    private static StoredFile Load(Row row)
    {
        var aggregate = StoredFile.Rehydrate(row.Snapshot);
        ((IVersioned)aggregate).SetVersion(row.Version);

        return aggregate;
    }

    private static StoredFileSnapshot Snapshot(StoredFile storedFile) => new()
    {
        Id = storedFile.Id,
        OwnerId = storedFile.OwnerId,
        Category = storedFile.Category,
        ObjectKey = storedFile.ObjectKey,
        Name = storedFile.Name,
        DeclaredMediaType = storedFile.DeclaredMediaType,
        Size = storedFile.Size,
        Checksum = storedFile.Checksum,
        State = storedFile.State,
        RegisteredAt = storedFile.RegisteredAt,
        AvailableAt = storedFile.AvailableAt,
        InspectionAttempts = storedFile.InspectionAttempts,
        NextInspectionAt = storedFile.NextInspectionAt,
        Tags = [.. storedFile.Tags.Select(tag => tag.Value)],
    };

    /// <summary>The values a file's own behaviour can change, compared by value.</summary>
    private static (StoredFileState, DateTimeOffset?, int, DateTimeOffset?, string) Key(StoredFileSnapshot snapshot) =>
        (snapshot.State,
            snapshot.AvailableAt,
            snapshot.InspectionAttempts,
            snapshot.NextInspectionAt,
            string.Join('\n', snapshot.Tags));

    private sealed record Row(StoredFileSnapshot Snapshot, uint Version);

    private sealed class Loaded(StoredFile aggregate, uint version)
    {
        public StoredFile Aggregate { get; } = aggregate;

        public uint Version { get; set; } = version;
    }
}

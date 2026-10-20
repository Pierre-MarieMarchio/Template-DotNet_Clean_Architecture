using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.Domain.Features.Reminders.Repositories;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class InMemoryReminderStore : IReminderRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Row> _committed = [];
    private readonly Dictionary<Guid, Loaded> _loaded = [];
    private readonly List<ReminderSnapshot> _writes = [];

    public int LastRequestedBatchSize { get; private set; }

    public int SaveAttempts { get; private set; }

    public IReadOnlyList<ReminderSnapshot> Writes => _writes;

    public void Seed(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        _committed[reminder.Id] = new Row(Snapshot(reminder), 1);
    }

    public void StartNewScope() => _loaded.Clear();

    public Reminder Committed(Guid id) => Load(_committed[id]);

    public void CancelBehindTheBack(Guid id)
    {
        var row = _committed[id];
        var cancelled = Load(row);
        cancelled.Cancel();

        _committed[id] = new Row(Snapshot(cancelled), row.Version + 1);
    }

    public Task<IReadOnlyList<Reminder>> GetDueAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken)
    {
        LastRequestedBatchSize = batchSize;

        var due = _committed.Values
            .Where(row => row.Snapshot.State == ReminderState.Pending
                && row.Snapshot.DueAt <= asOf
                && (row.Snapshot.NextAttemptAt is null || row.Snapshot.NextAttemptAt <= asOf))
            .OrderBy(row => row.Snapshot.NextAttemptAt ?? row.Snapshot.DueAt)
            .Take(batchSize)
            .Select(row => Track(row))
            .ToList();

        return Task.FromResult<IReadOnlyList<Reminder>>(due);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveAttempts++;

        var changed = _loaded.Values
            .Where(loaded => Key(Snapshot(loaded.Aggregate)) != Key(_committed[loaded.Aggregate.Id].Snapshot))
            .ToList();

        if (changed.Any(loaded => loaded.Version != _committed[loaded.Aggregate.Id].Version))
        {
            throw new ConcurrencyConflictException("A reminder was changed by somebody else.");
        }

        foreach (var loaded in changed)
        {
            var snapshot = Snapshot(loaded.Aggregate);
            uint version = loaded.Version + 1;

            _committed[loaded.Aggregate.Id] = new Row(snapshot, version);
            _writes.Add(snapshot);

            ((IVersioned)loaded.Aggregate).SetVersion(version);
            loaded.Version = version;
        }

        return Task.FromResult(changed.Count);
    }

    public Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult<Reminder?>(_committed.TryGetValue(id, out var row) ? Track(row) : null);

    public Task<IReadOnlyList<Reminder>> GetForTodoItemAsync(Guid todoItemId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Reminder>>(
            [.. _committed.Values
                .Where(row => row.Snapshot.TodoItemId == todoItemId)
                .Select(row => Track(row))]);

    public Task<IReadOnlyList<Reminder>> GetForOwnerAsync(UserId ownerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test of delivery removes an owner's reminders.");

    public void Add(Reminder reminder) => throw new NotSupportedException("No test of delivery schedules one.");

    public void Remove(Reminder reminder) => throw new NotSupportedException("No test of delivery removes one.");

    private Reminder Track(Row row)
    {
        if (_loaded.TryGetValue(row.Snapshot.Id, out var existing))
        {
            return existing.Aggregate;
        }

        var aggregate = Load(row);
        _loaded[row.Snapshot.Id] = new Loaded(aggregate, row.Version);

        return aggregate;
    }

    private static Reminder Load(Row row)
    {
        var aggregate = Reminder.Rehydrate(row.Snapshot);
        ((IVersioned)aggregate).SetVersion(row.Version);

        return aggregate;
    }

    private static ReminderSnapshot Snapshot(Reminder reminder) => new()
    {
        Id = reminder.Id,
        OwnerId = reminder.OwnerId,
        TodoListId = reminder.TodoListId,
        TodoItemId = reminder.TodoItemId,
        DueAt = reminder.DueAt,
        State = reminder.State,
        NotifiedAt = reminder.NotifiedAt,
        FailedAttempts = reminder.FailedAttempts,
        NextAttemptAt = reminder.NextAttemptAt,
    };

    private static (ReminderState, DateTimeOffset, DateTimeOffset?, int, DateTimeOffset?) Key(
        ReminderSnapshot snapshot) =>
        (snapshot.State, snapshot.DueAt, snapshot.NotifiedAt, snapshot.FailedAttempts, snapshot.NextAttemptAt);

    private sealed record Row(ReminderSnapshot Snapshot, uint Version);

    private sealed class Loaded(Reminder aggregate, uint version)
    {
        public Reminder Aggregate { get; } = aggregate;

        public uint Version { get; set; } = version;
    }
}

using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// A reminder store that keeps the two properties of the real one a delivery test depends on, and
/// nothing else: a save writes every changed aggregate or none of them, and a write made against a
/// version that has since moved is refused. It is repository and unit of work in one, as the EF
/// pair is over one context.
/// <para>
/// <b>It keeps a failed save's changes staged.</b> The real context does the same — after a
/// concurrency conflict the row stays modified, and every later save in that scope meets the same
/// conflict again. That is why a use case cannot step over a conflict and carry on in the same
/// scope, and a test that wants to say so needs a double that behaves this way.
/// </para>
/// <para>
/// <see cref="GetDueAsync"/> implements the contract <see cref="IReminderRepository"/> documents —
/// eligible reminders, in the order they became eligible, capped — because what a use case does with
/// a batch is only meaningful against a store that returns batches the way the real one does.
/// </para>
/// </summary>
internal sealed class InMemoryReminderStore : IReminderRepository, IUnitOfWork
{
    private readonly Dictionary<Guid, Row> _committed = [];
    private readonly Dictionary<Guid, Loaded> _loaded = [];
    private readonly List<ReminderSnapshot> _writes = [];

    /// <summary>The size the last batch was asked for.</summary>
    public int LastRequestedBatchSize { get; private set; }

    /// <summary>How many saves were attempted, successful or not.</summary>
    public int SaveAttempts { get; private set; }

    /// <summary>Every row state a successful save ever wrote, oldest first.</summary>
    public IReadOnlyList<ReminderSnapshot> Writes => _writes;

    /// <summary>Puts a reminder in the store as if it had been saved before.</summary>
    public void Seed(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        _committed[reminder.Id] = new Row(Snapshot(reminder), 1);
    }

    /// <summary>
    /// What a new request or job pass gets: nothing loaded yet, and so nothing staged. The identity
    /// map of the real context lives exactly as long as its scope, and a pass that failed leaves
    /// nothing behind for the next.
    /// </summary>
    public void StartNewScope() => _loaded.Clear();

    /// <summary>The state the store holds for a reminder, as a fresh aggregate would load it.</summary>
    public Reminder Committed(Guid id) => Load(_committed[id]);

    /// <summary>
    /// Another process cancels the reminder and commits, which moves its version under whoever
    /// loaded it earlier.
    /// </summary>
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

    /// <summary>The values a reminder's own behaviour can change, compared by value.</summary>
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

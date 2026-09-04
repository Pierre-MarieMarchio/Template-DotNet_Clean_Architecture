using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Reminders.Repositories;

/// <summary>
/// Loads and stages <see cref="Reminder"/> aggregates. Nothing here calls <c>SaveChangesAsync</c>: it
/// borrows the context and never owns the transaction. Committing belongs to <c>IUnitOfWork</c>.
/// <para>
/// Internal and sealed: it is an adapter for a port the domain layer declares, and nothing outside this
/// assembly has any business naming the type. Callers depend on <see cref="IReminderRepository"/>.
/// </para>
/// </summary>
internal sealed class ReminderRepository(
    AppDbContext context,
    IReminderMapper mapper,
    IReminderTracker tracker) : IReminderRepository
{
    public async Task<Reminder?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        // The identity map first. Two use cases in one request asking for the same reminder must get
        // the same object, or each would decide against its own copy and the flush would keep whichever
        // it saw last.
        if (tracker.Find(id) is { } alreadyLoaded)
        {
            return alreadyLoaded;
        }

        var record = await context.Reminders.FirstOrDefaultAsync(
            reminder => reminder.Id == id,
            cancellationToken);

        return record is null ? null : LoadOrTrack(record);
    }

    public async Task<IReadOnlyList<Reminder>> GetForTodoItemAsync(
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        var records = await context.Reminders
            .Where(reminder => reminder.TodoItemId == todoItemId)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public async Task<IReadOnlyList<Reminder>> GetDueAsync(
        DateTimeOffset asOf,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var records = await context.Reminders
            .Where(reminder => reminder.State == ReminderState.Pending && reminder.DueAt <= asOf)
            .OrderBy(reminder => reminder.DueAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return records.Select(LoadOrTrack).ToList();
    }

    public void Add(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        var record = mapper.ToNewRecord(reminder);

        context.Reminders.Add(record);

        // Tracked like any other: the flush pipeline will map onto this row again before the save, which
        // is how a mutation made after Add still lands.
        tracker.Track(reminder, record);
    }

    public void Remove(Reminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        // Ordinarily the row is already tracked, because a delete follows a load. The fallback attaches
        // a stub carrying the key and the version, so a caller who reconstructed an aggregate elsewhere
        // still gets a delete rather than a silent no-op — and still gets it checked against the token
        // it decided on, because attaching snapshots the current values as the original ones.
        var record = tracker.FindRecord(reminder.Id)
            ?? new ReminderRecord { Id = reminder.Id, Version = reminder.Version };

        context.Reminders.Remove(record);
        tracker.MarkRemoved(reminder, record);
    }

    /// <summary>
    /// Hands back the tracked aggregate for a row that is already known to this request, or maps and
    /// tracks a fresh one. Needed by every query that can return more than one row: unlike
    /// <see cref="GetAsync"/>, which only ever looks up one id, these can revisit a reminder already
    /// loaded earlier in the same request, and skipping the identity map would hand out a second,
    /// divergent copy of it.
    /// </summary>
    private Reminder LoadOrTrack(ReminderRecord record)
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

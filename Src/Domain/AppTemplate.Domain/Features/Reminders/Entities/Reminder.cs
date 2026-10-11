using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Events;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Entities;

/// <summary>A notification promised for a to-do item at a given instant; an aggregate of one,
/// since nothing about it needs to be consistent with anything else in the same transaction. It
/// names the item it is about rather than living inside <c>TodoList</c>: firing reads by due date
/// across every owner without loading a whole aggregate to reach one row.</summary>
public sealed class Reminder : AuditableAggregateRoot<Guid>, IOwnedAggregate
{
    /// <summary>The wait after the first failed attempt; each further failure doubles it.</summary>
    private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The longest wait between two attempts. Nothing here says a failure is permanent, so a
    /// reminder is retried for ever rather than abandoned: one that is retried still rings once
    /// the cause is fixed.
    /// </summary>
    private static readonly TimeSpan _longestRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    /// The one place the identity invariants are stated; both factories reach the aggregate through
    /// here, so scheduling and loading cannot disagree about what a reminder must carry.
    /// </summary>
    private Reminder(Guid id, UserId ownerId, Guid todoListId, Guid todoItemId, DateTimeOffset dueAt)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        if (id == Guid.Empty)
        {
            throw new DomainException("A reminder must have an id.");
        }

        if (todoListId == Guid.Empty || todoItemId == Guid.Empty)
        {
            throw new DomainException("A reminder must name the to-do item it is about.");
        }

        if (dueAt == default)
        {
            throw new DomainException("A reminder must have a due date.");
        }

        OwnerId = ownerId;
        TodoListId = todoListId;
        TodoItemId = todoItemId;
        DueAt = dueAt;
    }

    /// <summary>
    /// Copied from the list rather than reached through it, so firing can authorise and address a
    /// reminder without loading another feature's aggregate; safe because ownership never changes.
    /// </summary>
    public UserId OwnerId { get; private set; }

    /// <summary>Carried so that deleting a list can retire its reminders without first reading
    /// the items it is about to remove. A copy, correct only while an item cannot move between
    /// lists — the case here, <c>TodoItem.TodoListId</c> having no mutator; a feature that moves
    /// items must update it too, or it goes quietly stale.</summary>
    public Guid TodoListId { get; private set; }

    public Guid TodoItemId { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public ReminderState State { get; private set; } = ReminderState.Pending;

    /// <summary>When the notification actually went out.</summary>
    public DateTimeOffset? NotifiedAt { get; private set; }

    /// <summary>
    /// How many times notifying this reminder has failed since it was scheduled or last moved; a
    /// reminder that can never be delivered costs one attempt an hour rather than one a minute.
    /// </summary>
    public int FailedAttempts { get; private set; }

    /// <summary>The earliest instant the next attempt may be made after a failed one, or <c>null</c> when none has.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <param name="now">Injected rather than read from the clock, so the aggregate has no ambient
    /// dependency and its behaviour is reproducible in a test.</param>
    public static Reminder Schedule(
        UserId ownerId,
        Guid todoListId,
        Guid todoItemId,
        DateTimeOffset dueAt,
        DateTimeOffset now)
    {
        // Built first, so the constructor's invariants are what an ill-formed call meets. Being
        // due in the future is a precondition of scheduling, which the load path must not re-check.
        var reminder = new Reminder(Guid.CreateVersion7(), ownerId, todoListId, todoItemId, dueAt);

        if (dueAt <= now)
        {
            throw new DomainException("A reminder must be due in the future.");
        }

        return reminder;
    }

    /// <summary>Rebuilds a reminder from stored values; a row whose state and instants contradict
    /// each other is refused. That <see cref="DueAt"/> is in the future is deliberately not
    /// re-checked: it stops being true by the mere passing of time, and enforcing it here would
    /// refuse to load exactly the rows the firing query exists to find.</summary>
    public static Reminder Rehydrate(ReminderSnapshot stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.State == ReminderState.Fired && stored.NotifiedAt is null)
        {
            throw new DomainException("A fired reminder must record when it was notified.");
        }

        if (stored.State != ReminderState.Fired && stored.NotifiedAt is not null)
        {
            throw new DomainException("Only a fired reminder can record a notification instant.");
        }

        if (stored.FailedAttempts < 0)
        {
            throw new DomainException("A reminder cannot have a negative number of failed attempts.");
        }

        if (stored.NextAttemptAt is not null && stored.FailedAttempts == 0)
        {
            throw new DomainException("A reminder that has never failed cannot be waiting for a retry.");
        }

        return new Reminder(stored.Id, stored.OwnerId, stored.TodoListId, stored.TodoItemId, stored.DueAt)
        {
            State = stored.State,
            NotifiedAt = stored.NotifiedAt,
            FailedAttempts = stored.FailedAttempts,
            NextAttemptAt = stored.NextAttemptAt,
        };
    }

    /// <summary>Records that the notification went out, the only thing that retires a reminder for good.</summary>
    public void MarkNotified(DateTimeOffset notifiedAt)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can be marked as notified.");
        }

        State = ReminderState.Fired;
        NotifiedAt = notifiedAt;

        RaiseDomainEvent(new ReminderFiredDomainEvent(Id, OwnerId, TodoItemId, notifiedAt));
    }

    /// <summary>
    /// Records that an attempt to notify failed, and pushes the next one out. The wait doubles
    /// with each failure up to a ceiling, measured from now rather than the due date, so a
    /// reminder that has been failing for a day is not owed an immediate retry.
    /// </summary>
    public void RecordFailure(DateTimeOffset now)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can record a failed attempt.");
        }

        FailedAttempts++;
        NextAttemptAt = now + RetryDelayAfter(FailedAttempts);
    }

    /// <summary>The wait before the next attempt, once <paramref name="failedAttempts"/> have failed.</summary>
    public static TimeSpan RetryDelayAfter(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);

        var delay = _firstRetryDelay;

        for (int failure = 1; failure < failedAttempts && delay < _longestRetryDelay; failure++)
        {
            delay += delay;
        }

        return delay < _longestRetryDelay ? delay : _longestRetryDelay;
    }

    /// <summary>
    /// Calls the reminder off. Idempotent by shape — it assigns a state rather than moving one —
    /// so a redelivered cancellation is a no-op, and cancelling something already fired is refused.
    /// </summary>
    public void Cancel()
    {
        if (State == ReminderState.Fired)
        {
            throw new DomainException("A reminder that has already fired cannot be cancelled.");
        }

        State = ReminderState.Cancelled;
    }

    /// <summary>Moves a pending reminder to a new instant, under the same rule as scheduling.</summary>
    public void Reschedule(DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can be rescheduled.");
        }

        if (dueAt <= now)
        {
            throw new DomainException("A reminder must be due in the future.");
        }

        DueAt = dueAt;

        // A new instant is a new reminder as far as delivery goes: the failures counted so far were
        // against a moment that no longer applies.
        FailedAttempts = 0;
        NextAttemptAt = null;
    }
}

using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Events;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Model.Entities;

public sealed class Reminder : AuditableAggregateRoot<Guid>, IOwnedAggregate
{
    private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan _longestRetryDelay = TimeSpan.FromHours(1);

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

    public UserId OwnerId { get; private set; }

    public Guid TodoListId { get; private set; }

    public Guid TodoItemId { get; private set; }

    public DateTimeOffset DueAt { get; private set; }

    public ReminderState State { get; private set; } = ReminderState.Pending;

    public DateTimeOffset? NotifiedAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    public static Reminder Schedule(
        UserId ownerId,
        Guid todoListId,
        Guid todoItemId,
        DateTimeOffset dueAt,
        DateTimeOffset now)
    {
        var reminder = new Reminder(Guid.CreateVersion7(), ownerId, todoListId, todoItemId, dueAt);

        if (dueAt <= now)
        {
            throw new DomainException("A reminder must be due in the future.");
        }

        return reminder;
    }

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

    public void RecordFailure(DateTimeOffset now)
    {
        if (State != ReminderState.Pending)
        {
            throw new DomainException("Only a pending reminder can record a failed attempt.");
        }

        FailedAttempts++;
        NextAttemptAt = now + RetryDelayAfter(FailedAttempts);
    }

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

    public void Cancel()
    {
        if (State == ReminderState.Fired)
        {
            throw new DomainException("A reminder that has already fired cannot be cancelled.");
        }

        State = ReminderState.Cancelled;
    }

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

        FailedAttempts = 0;
        NextAttemptAt = null;
    }
}

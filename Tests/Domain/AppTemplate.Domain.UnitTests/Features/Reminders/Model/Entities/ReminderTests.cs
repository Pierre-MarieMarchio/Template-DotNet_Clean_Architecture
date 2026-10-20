using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.Events;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Reminders.Model.Entities;

public sealed class ReminderTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly Guid _todoListId = Guid.CreateVersion7();
    private static readonly Guid _todoItemId = Guid.CreateVersion7();

    private static Reminder AReminder(DateTimeOffset? dueAt = null, DateTimeOffset? scheduledAt = null) =>
        Reminder.Schedule(_ownerId, _todoListId, _todoItemId, dueAt ?? _now.AddHours(1), scheduledAt ?? _now);

    private static Reminder ARehydratedReminder(
        Guid? id = null,
        ReminderState state = ReminderState.Pending,
        DateTimeOffset? dueAt = null,
        DateTimeOffset? notifiedAt = null,
        int failedAttempts = 0,
        DateTimeOffset? nextAttemptAt = null) =>
        Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = _ownerId,
            TodoListId = _todoListId,
            TodoItemId = _todoItemId,
            DueAt = dueAt ?? _now.AddDays(-1),
            State = state,
            NotifiedAt = notifiedAt,
            FailedAttempts = failedAttempts,
            NextAttemptAt = nextAttemptAt,
        });

    [Fact]
    public void Schedule_Rejects_AnAbsentOwner()
    {
        var exception = Should.Throw<ArgumentNullException>(
            () => Reminder.Schedule(null!, _todoListId, _todoItemId, _now.AddHours(1), _now));

        exception.ParamName.ShouldBe("ownerId");
    }

    [Fact]
    public void Schedule_Rejects_AnEmptyTodoListId()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, Guid.Empty, _todoItemId, _now.AddHours(1), _now));

        exception.Message.ShouldContain("to-do item");
    }

    [Fact]
    public void Schedule_Rejects_AnEmptyTodoItemId()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, Guid.Empty, _now.AddHours(1), _now));

        exception.Message.ShouldContain("to-do item");
    }

    [Fact]
    public void Schedule_Rejects_TheDefaultDueDate()
    {
        var exception = Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, default, _now));

        exception.Message.ShouldContain("due date");
    }

    [Fact]
    public void Schedule_Rejects_ADueDateInThePast() =>
        Should.Throw<DomainException>(
            () => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, _now.AddMinutes(-1), _now));

    [Fact]
    public void Schedule_Rejects_ADueDateEqualToNow() =>
        Should.Throw<DomainException>(() => Reminder.Schedule(_ownerId, _todoListId, _todoItemId, _now, _now));

    [Fact]
    public void Schedule_Accepts_ADueDateInTheFuture()
    {
        var dueAt = _now.AddHours(1);

        var reminder = Reminder.Schedule(_ownerId, _todoListId, _todoItemId, dueAt, _now);

        reminder.Id.ShouldNotBe(Guid.Empty);
        reminder.OwnerId.ShouldBe(_ownerId);
        reminder.TodoListId.ShouldBe(_todoListId);
        reminder.TodoItemId.ShouldBe(_todoItemId);
        reminder.DueAt.ShouldBe(dueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
        reminder.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public void Schedule_GivesEveryReminderADistinctId() => AReminder().Id.ShouldNotBe(AReminder().Id);

    [Fact]
    public void Rehydrate_Rejects_TheDefaultDueDate() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = default,
                State = ReminderState.Pending,
                NotifiedAt = null,
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));

    [Fact]
    public void Rehydrate_Rejects_AnAbsentSnapshot() =>
        Should.Throw<ArgumentNullException>(() => Reminder.Rehydrate(null!));

    [Fact]
    public void Rehydrate_Rejects_AnAbsentOwner() =>
        Should.Throw<ArgumentNullException>(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = null!,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = _now.AddHours(1),
                State = ReminderState.Pending,
                NotifiedAt = null,
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));

    [Fact]
    public void Rehydrate_Rejects_AnEmptyId() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.Empty,
                OwnerId = _ownerId,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = _now.AddHours(1),
                State = ReminderState.Pending,
                NotifiedAt = null,
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));

    [Fact]
    public void Rehydrate_DoesNotRecheckThatTheDueDateIsInTheFuture()
    {
        var overdueBy = _now.AddDays(-1);

        var reminder = Should.NotThrow(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = overdueBy,
                State = ReminderState.Pending,
                NotifiedAt = null,
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));

        reminder.DueAt.ShouldBe(overdueBy);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void Rehydrate_RestoresTheStoredIdentityAndValues()
    {
        var id = Guid.CreateVersion7();
        var dueAt = _now.AddHours(2);

        var reminder = Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = id,
            OwnerId = _ownerId,
            TodoListId = _todoListId,
            TodoItemId = _todoItemId,
            DueAt = dueAt,
            State = ReminderState.Pending,
            NotifiedAt = null,
            FailedAttempts = 0,
            NextAttemptAt = null,
        });

        reminder.Id.ShouldBe(id);
        reminder.OwnerId.ShouldBe(_ownerId);
        reminder.TodoListId.ShouldBe(_todoListId);
        reminder.TodoItemId.ShouldBe(_todoItemId);
        reminder.DueAt.ShouldBe(dueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
        reminder.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public void Rehydrate_RaisesNoDomainEvent() =>
        ARehydratedReminder().DomainEvents.ShouldBeEmpty();

    [Fact]
    public void MarkNotified_Rejects_ACancelledReminder()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.Cancel();

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now));
    }

    [Fact]
    public void MarkNotified_Rejects_AReminderAlreadyFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now.AddMinutes(1)));
    }

    [Fact]
    public void MarkNotified_Transitions_APendingReminderToFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        var notifiedAt = _now.AddSeconds(1);

        reminder.MarkNotified(notifiedAt);

        reminder.State.ShouldBe(ReminderState.Fired);
        reminder.NotifiedAt.ShouldBe(notifiedAt);
    }

    [Fact]
    public void Cancel_MovesAPendingReminderToCancelled()
    {
        var reminder = AReminder();

        reminder.Cancel();

        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public void Cancel_Rejects_AReminderAlreadyFired()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.Cancel());
    }

    [Fact]
    public void Cancel_IsIdempotent()
    {
        var reminder = AReminder();

        reminder.Cancel();
        Should.NotThrow(() => reminder.Cancel());

        reminder.State.ShouldBe(ReminderState.Cancelled);
    }

    [Fact]
    public void Reschedule_MovesAPendingReminderToTheNewDueDate()
    {
        var reminder = AReminder();
        var newDueAt = _now.AddDays(1);

        reminder.Reschedule(newDueAt, _now);

        reminder.DueAt.ShouldBe(newDueAt);
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void Reschedule_Rejects_ADueDateInThePast()
    {
        var reminder = AReminder();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddMinutes(-1), _now));
    }

    [Fact]
    public void Reschedule_Rejects_ADueDateEqualToNow()
    {
        var reminder = AReminder();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now, _now));
    }

    [Fact]
    public void Reschedule_Rejects_ACancelledReminder()
    {
        var reminder = AReminder();
        reminder.Cancel();

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddDays(1), _now));
    }

    [Fact]
    public void Reschedule_Rejects_AFiredReminder()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.MarkNotified(_now);

        Should.Throw<DomainException>(() => reminder.Reschedule(_now.AddDays(1), _now));
    }

    [Fact]
    public void RecordFailure_CountsTheAttemptAndWaitsBeforeTheNext()
    {
        var reminder = ARehydratedReminder();

        reminder.RecordFailure(_now);

        reminder.FailedAttempts.ShouldBe(1);
        reminder.NextAttemptAt.ShouldBe(_now + TimeSpan.FromMinutes(1));
        reminder.State.ShouldBe(ReminderState.Pending);
    }

    [Fact]
    public void RecordFailure_LengthensTheWaitWithEachFailure()
    {
        var reminder = ARehydratedReminder();

        reminder.RecordFailure(_now);
        reminder.RecordFailure(_now.AddMinutes(1));

        reminder.FailedAttempts.ShouldBe(2);
        reminder.NextAttemptAt.ShouldBe(_now.AddMinutes(1) + TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(8, 60)]
    [InlineData(1_000_000, 60)]
    public void RetryDelayAfter_DoublesFromAMinuteUpToAnHour(int failedAttempts, int expectedMinutes) =>
        Reminder.RetryDelayAfter(failedAttempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));

    [Fact]
    public void RetryDelayAfter_Rejects_NoFailureAtAll() =>
        Should.Throw<ArgumentOutOfRangeException>(() => Reminder.RetryDelayAfter(0));

    [Theory]
    [InlineData(ReminderState.Fired)]
    [InlineData(ReminderState.Cancelled)]
    public void RecordFailure_Rejects_AReminderThatIsNoLongerPending(ReminderState state)
    {
        var reminder = ARehydratedReminder(
            state: state,
            notifiedAt: state == ReminderState.Fired ? _now.AddMinutes(-1) : null);

        Should.Throw<DomainException>(() => reminder.RecordFailure(_now));
    }

    [Fact]
    public void Reschedule_ForgetsThePreviousFailures()
    {
        var reminder = ARehydratedReminder(failedAttempts: 4, nextAttemptAt: _now.AddMinutes(8));

        reminder.Reschedule(_now.AddDays(1), _now);

        reminder.FailedAttempts.ShouldBe(0);
        reminder.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void Rehydrate_Rejects_ANegativeFailureCount() =>
        Should.Throw<DomainException>(() => ARehydratedReminder(failedAttempts: -1));

    [Fact]
    public void Rehydrate_Rejects_ARetryTimeForAReminderThatNeverFailed() =>
        Should.Throw<DomainException>(() => ARehydratedReminder(failedAttempts: 0, nextAttemptAt: _now));

    [Fact]
    public void Rehydrate_Restores_TheRetryState()
    {
        var reminder = ARehydratedReminder(failedAttempts: 3, nextAttemptAt: _now.AddMinutes(4));

        reminder.FailedAttempts.ShouldBe(3);
        reminder.NextAttemptAt.ShouldBe(_now.AddMinutes(4));
    }

    [Fact]
    public void MarkNotified_RaisesAReminderFiredDomainEvent_WithTheRemindersOwnValues()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        var notifiedAt = _now.AddSeconds(1);

        reminder.MarkNotified(notifiedAt);

        var raised = reminder.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ReminderFiredDomainEvent>();
        raised.ReminderId.ShouldBe(reminder.Id);
        raised.OwnerId.ShouldBe(_ownerId);
        raised.TodoItemId.ShouldBe(_todoItemId);
        raised.OccurredOn.ShouldBe(notifiedAt);
    }

    [Fact]
    public void MarkNotified_RaisesNoAdditionalEvent_WhenARepeatCallIsRejected()
    {
        var reminder = ARehydratedReminder(dueAt: _now.AddDays(-1));
        reminder.MarkNotified(_now);
        reminder.ClearDomainEvents();

        Should.Throw<DomainException>(() => reminder.MarkNotified(_now.AddMinutes(1)));

        reminder.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Schedule_RaisesNoDomainEvent() => AReminder().DomainEvents.ShouldBeEmpty();

    [Fact]
    public void Rehydrate_Rejects_AFiredReminderWithNoNotificationInstant() =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = _now.AddDays(-1),
                State = ReminderState.Fired,
                NotifiedAt = null,
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));

    [Theory]
    [InlineData(ReminderState.Pending)]
    [InlineData(ReminderState.Cancelled)]
    public void Rehydrate_Rejects_ANotificationInstantOnAReminderThatNeverFired(ReminderState state) =>
        Should.Throw<DomainException>(
            () => Reminder.Rehydrate(new ReminderSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                TodoListId = _todoListId,
                TodoItemId = _todoItemId,
                DueAt = _now.AddDays(-1),
                State = state,
                NotifiedAt = _now.AddDays(-1),
                FailedAttempts = 0,
                NextAttemptAt = null,
            }));
}

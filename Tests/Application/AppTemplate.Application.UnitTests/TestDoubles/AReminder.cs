using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal static class AReminder
{
    internal static Reminder OwnedBy(
        UserId ownerId,
        DateTimeOffset? dueAt = null,
        Guid? todoListId = null,
        Guid? todoItemId = null)
    {
        var reminder = Reminder.Schedule(
            ownerId,
            todoListId ?? Guid.CreateVersion7(),
            todoItemId ?? Guid.CreateVersion7(),
            dueAt ?? StubDateTimeProvider.DefaultInstant.AddHours(1),
            StubDateTimeProvider.DefaultInstant);

        reminder.ClearDomainEvents();

        return reminder;
    }

    internal static Reminder OwnedBySomebodyElseThan(UserId notThisUserId)
    {
        var otherOwnerId = UserId.Create(Guid.CreateVersion7());

        if (otherOwnerId == notThisUserId)
        {
            throw new InvalidOperationException("Guid.CreateVersion7 produced a collision.");
        }

        return OwnedBy(otherOwnerId);
    }

    internal static Reminder OwnedByAtVersion(UserId ownerId, uint version)
    {
        var reminder = OwnedBy(ownerId);
        ((IVersioned)reminder).SetVersion(version);

        return reminder;
    }

    internal static Reminder Rehydrated(
        UserId ownerId,
        DateTimeOffset dueAt,
        ReminderState state = ReminderState.Pending,
        DateTimeOffset? notifiedAt = null,
        int failedAttempts = 0,
        DateTimeOffset? nextAttemptAt = null,
        Guid? id = null,
        Guid? todoListId = null,
        Guid? todoItemId = null) =>
        Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId,
            TodoListId = todoListId ?? Guid.CreateVersion7(),
            TodoItemId = todoItemId ?? Guid.CreateVersion7(),
            DueAt = dueAt,
            State = state,
            NotifiedAt = notifiedAt,
            FailedAttempts = failedAttempts,
            NextAttemptAt = nextAttemptAt,
        });
}

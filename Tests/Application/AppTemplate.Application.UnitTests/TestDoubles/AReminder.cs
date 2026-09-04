using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Application.UnitTests.TestDoubles;

/// <summary>
/// Builds real aggregates, never fakes — same rationale as <see cref="ATodoList"/>.
/// </summary>
internal static class AReminder
{
    /// <summary>A reminder scheduled the normal way, due after <see cref="FixedDateTimeProvider.DefaultInstant"/>.</summary>
    internal static Reminder OwnedBy(
        Guid ownerId,
        DateTimeOffset? dueAt = null,
        Guid? todoListId = null,
        Guid? todoItemId = null)
    {
        var reminder = Reminder.Schedule(
            ownerId,
            todoListId ?? Guid.CreateVersion7(),
            todoItemId ?? Guid.CreateVersion7(),
            dueAt ?? FixedDateTimeProvider.DefaultInstant.AddHours(1),
            FixedDateTimeProvider.DefaultInstant);

        reminder.ClearDomainEvents();

        return reminder;
    }

    internal static Reminder OwnedBySomebodyElseThan(Guid notThisUserId)
    {
        var otherOwnerId = Guid.CreateVersion7();

        if (otherOwnerId == notThisUserId)
        {
            throw new InvalidOperationException("Guid.CreateVersion7 produced a collision.");
        }

        return OwnedBy(otherOwnerId);
    }

    /// <summary>Placed at <paramref name="version"/> the way the store places a freshly loaded
    /// aggregate. Goes through <see cref="IVersioned"/> because that is the only way anything
    /// writes a version.</summary>
    internal static Reminder OwnedByAtVersion(Guid ownerId, uint version)
    {
        var reminder = OwnedBy(ownerId);
        ((IVersioned)reminder).SetVersion(version);

        return reminder;
    }

    /// <summary>
    /// Rehydrated directly in whatever state is asked for, the way a store would load it —
    /// <see cref="Reminder.Schedule"/> cannot produce a reminder that is already due, already
    /// claimed, or already fired, since scheduling refuses a due date in the past.
    /// </summary>
    internal static Reminder Rehydrated(
        Guid ownerId,
        DateTimeOffset dueAt,
        ReminderState state = ReminderState.Pending,
        DateTimeOffset? claimedAt = null,
        DateTimeOffset? notifiedAt = null,
        Guid? id = null,
        Guid? todoListId = null,
        Guid? todoItemId = null) =>
        Reminder.Rehydrate(
            id ?? Guid.CreateVersion7(),
            ownerId,
            todoListId ?? Guid.CreateVersion7(),
            todoItemId ?? Guid.CreateVersion7(),
            dueAt,
            state,
            claimedAt,
            notifiedAt);
}

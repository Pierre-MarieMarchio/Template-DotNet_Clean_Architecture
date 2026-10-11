using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Entities;

/// <summary>The stored values <see cref="Reminder.Rehydrate"/> rebuilds a reminder from. A parameter
/// object rather than nine parameters, because transposing <see cref="TodoListId"/> and
/// <see cref="TodoItemId"/> compiles, and naming every value makes that mistake impossible. Every
/// member is <c>required</c>, including the nullable instants: <c>NotifiedAt = null</c> is a
/// decision, where an omitted argument would have been an oversight.</summary>
public sealed class ReminderSnapshot
{
    /// <summary>The reminder's identity.</summary>
    public required Guid Id { get; init; }

    /// <summary>Who the reminder belongs to.</summary>
    public required UserId OwnerId { get; init; }

    /// <summary>The list owning the item this reminder is for.</summary>
    public required Guid TodoListId { get; init; }

    /// <summary>The item this reminder is for.</summary>
    public required Guid TodoItemId { get; init; }

    /// <summary>When the reminder comes up. Not re-checked against the clock on the way in.</summary>
    public required DateTimeOffset DueAt { get; init; }

    /// <summary>How far the reminder got.</summary>
    public required ReminderState State { get; init; }

    /// <summary>When it was notified, or <c>null</c>.</summary>
    public required DateTimeOffset? NotifiedAt { get; init; }

    /// <summary>How many attempts to notify have failed.</summary>
    public required int FailedAttempts { get; init; }

    /// <summary>The earliest instant the next attempt may be made, or <c>null</c>.</summary>
    public required DateTimeOffset? NextAttemptAt { get; init; }
}

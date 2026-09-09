using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Entities;

/// <summary>
/// The stored values <see cref="Reminder.Rehydrate"/> rebuilds a reminder from.
/// <para>
/// A parameter object rather than eight parameters, because four of them are a <see cref="Guid"/> or
/// a <see cref="DateTimeOffset"/> and three of the ids sit next to each other: transposing
/// <see cref="TodoListId"/> and <see cref="TodoItemId"/> compiles, and produces a reminder attached
/// to an item that is not the one the row named. Naming every value at the call site is what makes
/// that mistake impossible rather than unlikely.
/// </para>
/// <para>
/// Every member is <c>required</c>, including the two nullable instants: a caller that has no claim
/// to report says so by writing <c>ClaimedAt = null</c>, which is a decision, where an omitted
/// argument would have been an oversight.
/// </para>
/// </summary>
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

    /// <summary>When a host took responsibility for firing it, or <c>null</c>.</summary>
    public required DateTimeOffset? ClaimedAt { get; init; }

    /// <summary>When it was notified, or <c>null</c>.</summary>
    public required DateTimeOffset? NotifiedAt { get; init; }
}

using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

/// <summary>
/// Builds a reminder aggregate in which <b>every</b> piece of state is set to a value distinguishable
/// from its type's default.
/// </summary>
/// <remarks>
/// A round-trip assertion that compares <c>null</c> against <c>null</c>, or the enum's default member
/// against itself, passes for a property the mapper never copied — so a sample built from a freshly
/// scheduled reminder would silently exempt exactly the properties a firing host sets: <c>ClaimedAt</c>,
/// <c>NotifiedAt</c>, a non-<c>Pending</c> <c>State</c>. This builder goes through
/// <see cref="Reminder.Rehydrate"/> instead of the lifecycle methods, so it can put the aggregate
/// straight into that fired shape without a clock to advance.
/// </remarks>
internal static class AReminderAggregate
{
    internal static readonly Guid OwnerId = new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a61");
    internal static readonly Guid TodoListId = new("0199a3c4-3333-7000-8000-000000000001");
    internal static readonly Guid TodoItemId = new("0199a3c4-4444-7000-8000-000000000001");
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555556");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaab");

    internal static readonly DateTimeOffset DueAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset ClaimedAt = new(2026, 6, 1, 8, 55, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset NotifiedAt = new(2026, 6, 1, 9, 0, 5, TimeSpan.Zero);
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);

    /// <summary>Non-zero, so a mapper that dropped the concurrency token is visible.</summary>
    internal const uint Version = 987_654u;

    /// <summary>Not <see cref="ReminderState.Pending"/>, the enum's default member.</summary>
    internal const ReminderState State = ReminderState.Fired;

    internal static Guid ReminderId { get; } = new("0199a3c4-1111-7000-8000-000000000099");

    /// <summary>A fully populated aggregate, as if it had just been loaded after firing.</summary>
    internal static Reminder FullyPopulated()
    {
        var aggregate = Reminder.Rehydrate(
            ReminderId,
            OwnerId,
            TodoListId,
            TodoItemId,
            DueAt,
            State,
            ClaimedAt,
            NotifiedAt);

        ((IVersioned)aggregate).SetVersion(Version);
        ((IAuditable)aggregate).SetCreated(CreatedAt, CreatedBy);
        ((IAuditable)aggregate).SetLastModified(LastModifiedAt, LastModifiedBy);

        return aggregate;
    }

    /// <summary>
    /// The same reminder, with <b>every</b> domain-owned value different from <see cref="FullyPopulated"/>
    /// — including the ones only an update can move. The id is deliberately unchanged: a different id
    /// would make this a fresh insert, going through <c>ToNewRecord</c> instead of the update path this
    /// sample exists to exercise.
    /// </summary>
    internal static Reminder DifferentInEveryDomainOwnedValue()
    {
        var aggregate = Reminder.Rehydrate(
            ReminderId,
            OtherOwnerId,
            OtherTodoListId,
            OtherTodoItemId,
            OtherDueAt,
            OtherState,
            OtherClaimedAt,
            OtherNotifiedAt);

        ((IVersioned)aggregate).SetVersion(OtherVersion);
        ((IAuditable)aggregate).SetCreated(OtherCreatedAt, OtherCreatedBy);
        ((IAuditable)aggregate).SetLastModified(OtherLastModifiedAt, OtherLastModifiedBy);

        return aggregate;
    }

    // ---- The second, entirely different set of values -------------------------------------------

    internal static readonly Guid OtherOwnerId = new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e61");
    internal static readonly Guid OtherTodoListId = new("0199a3c4-3333-7000-8000-000000000002");
    internal static readonly Guid OtherTodoItemId = new("0199a3c4-4444-7000-8000-000000000002");
    internal static readonly Guid OtherCreatedBy = new("22222222-3333-4444-5555-666666666667");
    internal static readonly Guid OtherLastModifiedBy = new("77777777-8888-9999-aaaa-bbbbbbbbbbbc");

    internal static readonly DateTimeOffset OtherDueAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherClaimedAt = new(2025, 7, 8, 9, 5, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherCreatedAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherLastModifiedAt = new(2025, 7, 9, 12, 13, 14, TimeSpan.Zero);

    internal const uint OtherVersion = 123_456u;

    // Cancelled rather than Fired, so it differs from FullyPopulated's State. Rehydrate ties
    // NotifiedAt to a Fired state, so a state that is not Fired must carry no notification instant.
    internal const ReminderState OtherState = ReminderState.Cancelled;

    internal static DateTimeOffset? OtherNotifiedAt => null;
}

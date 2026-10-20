using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Reminders;

internal static class AReminderAggregate
{
    internal static readonly UserId OwnerId = UserId.Create(new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a61"));
    internal static readonly Guid TodoListId = new("0199a3c4-3333-7000-8000-000000000001");
    internal static readonly Guid TodoItemId = new("0199a3c4-4444-7000-8000-000000000001");
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555556");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaab");

    internal static readonly DateTimeOffset DueAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset NotifiedAt = new(2026, 6, 1, 9, 0, 5, TimeSpan.Zero);
    internal static readonly DateTimeOffset NextAttemptAt = new(2026, 6, 1, 9, 2, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);

    internal const int FailedAttempts = 3;

    internal const uint Version = 987_654u;

    internal const ReminderState State = ReminderState.Fired;

    internal static Guid ReminderId { get; } = new("0199a3c4-1111-7000-8000-000000000099");

    internal static Reminder FullyPopulated()
    {
        var aggregate = Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = ReminderId,
            OwnerId = OwnerId,
            TodoListId = TodoListId,
            TodoItemId = TodoItemId,
            DueAt = DueAt,
            State = State,
            NotifiedAt = NotifiedAt,
            FailedAttempts = FailedAttempts,
            NextAttemptAt = NextAttemptAt,
        });

        ((IVersioned)aggregate).SetVersion(Version);
        ((IAuditable)aggregate).SetCreated(CreatedAt, CreatedBy);
        ((IAuditable)aggregate).SetLastModified(LastModifiedAt, LastModifiedBy);

        return aggregate;
    }

    internal static Reminder DifferentInEveryDomainOwnedValue()
    {
        var aggregate = Reminder.Rehydrate(new ReminderSnapshot
        {
            Id = ReminderId,
            OwnerId = OtherOwnerId,
            TodoListId = OtherTodoListId,
            TodoItemId = OtherTodoItemId,
            DueAt = OtherDueAt,
            State = OtherState,
            NotifiedAt = OtherNotifiedAt,
            FailedAttempts = OtherFailedAttempts,
            NextAttemptAt = OtherNextAttemptAt,
        });

        ((IVersioned)aggregate).SetVersion(OtherVersion);
        ((IAuditable)aggregate).SetCreated(OtherCreatedAt, OtherCreatedBy);
        ((IAuditable)aggregate).SetLastModified(OtherLastModifiedAt, OtherLastModifiedBy);

        return aggregate;
    }

    internal static readonly UserId OtherOwnerId = UserId.Create(new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e61"));
    internal static readonly Guid OtherTodoListId = new("0199a3c4-3333-7000-8000-000000000002");
    internal static readonly Guid OtherTodoItemId = new("0199a3c4-4444-7000-8000-000000000002");
    internal static readonly Guid OtherCreatedBy = new("22222222-3333-4444-5555-666666666667");
    internal static readonly Guid OtherLastModifiedBy = new("77777777-8888-9999-aaaa-bbbbbbbbbbbc");

    internal static readonly DateTimeOffset OtherDueAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherCreatedAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherLastModifiedAt = new(2025, 7, 9, 12, 13, 14, TimeSpan.Zero);

    internal const uint OtherVersion = 123_456u;

    internal const ReminderState OtherState = ReminderState.Cancelled;

    internal const int OtherFailedAttempts = 5;

    internal static readonly DateTimeOffset OtherNextAttemptAt = new(2025, 7, 8, 10, 11, 12, TimeSpan.Zero);

    internal static DateTimeOffset? OtherNotifiedAt => null;
}

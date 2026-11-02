using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Reminders.Model.Entities;

public sealed class ReminderSnapshot
{
    public required Guid Id { get; init; }

    public required UserId OwnerId { get; init; }

    public required Guid TodoListId { get; init; }

    public required Guid TodoItemId { get; init; }

    public required DateTimeOffset DueAt { get; init; }

    public required ReminderState State { get; init; }

    public required DateTimeOffset? NotifiedAt { get; init; }

    public required int FailedAttempts { get; init; }

    public required DateTimeOffset? NextAttemptAt { get; init; }
}

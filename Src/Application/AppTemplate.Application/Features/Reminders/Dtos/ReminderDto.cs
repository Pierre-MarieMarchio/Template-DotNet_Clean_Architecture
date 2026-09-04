using AppTemplate.Domain.Features.Reminders.ValueObjects;

namespace AppTemplate.Application.Features.Reminders.Dtos;

/// <param name="TodoListId">Carried alongside <paramref name="TodoItemId"/> so a client can show
/// which list a reminder belongs to without a second call.</param>
public sealed record ReminderDto(
    Guid Id,
    Guid TodoListId,
    Guid TodoItemId,
    DateTimeOffset DueAt,
    ReminderState State,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? NotifiedAt);

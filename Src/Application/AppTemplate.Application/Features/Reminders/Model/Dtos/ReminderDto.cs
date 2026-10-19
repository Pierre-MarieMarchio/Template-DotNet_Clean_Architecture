using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Application.Features.Reminders.Model.Dtos;

/// <param name="TodoListId">Carried alongside <paramref name="TodoItemId"/> so a client can show
/// which list a reminder belongs to without a second call.</param>
public sealed record ReminderDto(
    Guid Id,
    Guid TodoListId,
    Guid TodoItemId,
    DateTimeOffset DueAt,
    ReminderState State,
    DateTimeOffset? NotifiedAt);

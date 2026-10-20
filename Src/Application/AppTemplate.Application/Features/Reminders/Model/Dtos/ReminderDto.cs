using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;

namespace AppTemplate.Application.Features.Reminders.Model.Dtos;

public sealed record ReminderDto(
    Guid Id,
    Guid TodoListId,
    Guid TodoItemId,
    DateTimeOffset DueAt,
    ReminderState State,
    DateTimeOffset? NotifiedAt);

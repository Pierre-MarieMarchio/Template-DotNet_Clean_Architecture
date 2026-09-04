namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;

public sealed record ScheduleReminderCommand(Guid TodoListId, Guid TodoItemId, DateTimeOffset DueAt);

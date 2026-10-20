namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

public sealed record GetRemindersQuery(Guid TodoListId, Guid TodoItemId);

namespace AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;

/// <param name="TodoListId">The list the item is expected to belong to. Carried so that reaching an
/// item through the wrong list answers nothing, the same way every other route into an item
/// behaves — an id that appears in a URL and is never checked teaches a caller it does not
/// matter.</param>
public sealed record GetRemindersQuery(Guid TodoListId, Guid TodoItemId);

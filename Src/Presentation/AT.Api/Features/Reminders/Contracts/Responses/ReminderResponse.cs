namespace AppTemplate.Api.Features.Reminders.Contracts.Responses;

/// <summary>One reminder, also the body of every write that addresses it.</summary>
/// <param name="TodoListId">The list the reminder's item belongs to.</param>
/// <param name="Status">
/// <c>pending</c>, <c>fired</c> or <c>cancelled</c>, never going back to an earlier value.
/// </param>
/// <param name="NotifiedAt">When the reminder actually fired, or <c>null</c> before it does.</param>
public sealed record ReminderResponse(
    Guid Id,
    Guid TodoListId,
    Guid TodoItemId,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? NotifiedAt);

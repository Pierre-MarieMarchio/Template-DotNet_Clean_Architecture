namespace AppTemplate.Api.Features.Reminders.Contracts.Responses;

/// <summary>
/// The wire shape of one reminder, and the body of every write that addresses one: a caller never
/// has to re-read what it just changed to keep writing.
/// </summary>
/// <param name="TodoListId">Carried alongside <paramref name="TodoItemId"/> so a client can show
/// which list the reminder belongs to without a second call.</param>
/// <param name="Status">One of <c>pending</c>, <c>fired</c> or <c>cancelled</c> — a one-way life
/// that never goes back to an earlier value.</param>
/// <param name="ClaimedAt">Set while a firing host is mid-attempt at notifying. Informational: a
/// caller has no action to take on it.</param>
/// <param name="NotifiedAt">When the reminder actually fired, or <c>null</c> before it does.</param>
public sealed record ReminderResponse(
    Guid Id,
    Guid TodoListId,
    Guid TodoItemId,
    DateTimeOffset DueAt,
    string Status,
    DateTimeOffset? ClaimedAt,
    DateTimeOffset? NotifiedAt);

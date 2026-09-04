namespace AppTemplate.Api.Features.Reminders.Contracts.Responses;

/// <summary>
/// Every reminder scheduled for one item, wrapped in an object: an array at the top level can never
/// gain a sibling field without breaking its callers, which is the same reason nothing here answers
/// with a bare scalar either.
/// </summary>
/// <param name="Reminders">Ordered by due date.</param>
public sealed record RemindersResponse(IReadOnlyList<ReminderResponse> Reminders);

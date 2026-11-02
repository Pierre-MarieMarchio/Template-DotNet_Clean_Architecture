namespace AppTemplate.Api.Features.Reminders.Contracts.Responses;

/// <summary>Every reminder scheduled for one item.</summary>
/// <param name="Reminders">Ordered by due date.</param>
public sealed record RemindersResponse(IReadOnlyList<ReminderResponse> Reminders);

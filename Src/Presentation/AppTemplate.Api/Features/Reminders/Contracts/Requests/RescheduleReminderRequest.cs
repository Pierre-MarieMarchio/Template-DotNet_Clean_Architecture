namespace AppTemplate.Api.Features.Reminders.Contracts.Requests;

/// <summary>Carries no reminder id: that travels in the route.</summary>
/// <param name="DueAt">The new due date. Must be in the future at the moment the use case runs.</param>
public sealed record RescheduleReminderRequest(DateTimeOffset DueAt);

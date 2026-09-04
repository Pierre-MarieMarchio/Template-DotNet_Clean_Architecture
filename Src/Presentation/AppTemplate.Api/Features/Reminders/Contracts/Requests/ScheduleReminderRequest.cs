namespace AppTemplate.Api.Features.Reminders.Contracts.Requests;

/// <summary>Carries no list or item id: both travel in the route.</summary>
/// <param name="DueAt">Must be in the future at the moment the use case runs.</param>
public sealed record ScheduleReminderRequest(DateTimeOffset DueAt);

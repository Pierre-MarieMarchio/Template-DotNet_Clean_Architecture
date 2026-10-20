using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Reminders.Contracts.Requests;

/// <summary>Carries no reminder id: that travels in the route.</summary>
/// <param name="DueAt">The new due date.</param>
public sealed record RescheduleReminderRequest([property: JsonRequired] DateTimeOffset DueAt);

using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Reminders.Contracts.Requests;

/// <summary>Carries no list or item id: both travel in the route.</summary>
/// <param name="DueAt">
/// Must be in the future at the moment the use case runs. Required in the body rather than
/// merely documented: an omitted date binds to <c>0001-01-01</c>, which the use case then
/// refuses for being in the past -- an answer that describes a date the caller never sent.
/// </param>
public sealed record ScheduleReminderRequest([property: JsonRequired] DateTimeOffset DueAt);

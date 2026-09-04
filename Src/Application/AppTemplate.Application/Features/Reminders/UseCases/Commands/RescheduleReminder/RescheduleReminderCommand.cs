using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional reschedule.
/// </param>
public sealed record RescheduleReminderCommand(
    Guid ReminderId,
    DateTimeOffset DueAt,
    VersionPrecondition? Precondition = null);

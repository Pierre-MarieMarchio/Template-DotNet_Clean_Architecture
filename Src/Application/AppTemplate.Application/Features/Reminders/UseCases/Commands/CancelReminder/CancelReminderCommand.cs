using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional cancel.
/// </param>
public sealed record CancelReminderCommand(Guid ReminderId, VersionPrecondition? Precondition = null);

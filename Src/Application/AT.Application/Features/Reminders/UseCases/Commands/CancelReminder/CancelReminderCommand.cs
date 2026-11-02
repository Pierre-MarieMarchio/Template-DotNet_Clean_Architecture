using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed record CancelReminderCommand(Guid ReminderId, VersionPrecondition? Precondition = null);

using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public sealed record RescheduleReminderCommand(
    Guid ReminderId,
    DateTimeOffset DueAt,
    VersionPrecondition? Precondition = null);

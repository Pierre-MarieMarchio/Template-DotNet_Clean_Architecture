using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Reminders.Dtos;
using AppTemplate.Application.Features.Reminders.Mapping;
using AppTemplate.Application.Features.Reminders.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public sealed class RescheduleReminderUseCase(
    IReminderService reminders,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IValidator<RescheduleReminderCommand> validator) : IRescheduleReminderUseCase
{
    public async Task<Result<Versioned<ReminderDto>>> ExecuteAsync(
        RescheduleReminderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<ReminderDto>>();
        }

        var access = await reminders.LoadOwnedAsync(command.ReminderId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<ReminderDto>>();
        }

        var reminder = access.Value;

        // Caught: a reminder no longer Pending, or a due date in the past, both depend on state
        // the validator above has no way to check.
        var reschedule = DomainGuard.Try(() => reminder.Reschedule(command.DueAt, dateTimeProvider.UtcNow));

        if (reschedule.IsFailure)
        {
            return reschedule.To<Versioned<ReminderDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReminderDtoMapping.ToVersioned(reminder);
    }
}

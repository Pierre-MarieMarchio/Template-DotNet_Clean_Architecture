using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.Model.Mapping;
using AppTemplate.Application.Features.Reminders.Services;
using Mediator;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;

public sealed record RescheduleReminderCommand(
    Guid ReminderId,
    DateTimeOffset DueAt,
    VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<ReminderDto>>>;

public sealed class RescheduleReminderCommandHandler(
    IReminderService reminders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RescheduleReminderCommand, Result<Versioned<ReminderDto>>>
{
    public async ValueTask<Result<Versioned<ReminderDto>>> Handle(
        RescheduleReminderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await reminders.LoadOwnedAsync(command.ReminderId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access.To<Versioned<ReminderDto>>();
        }

        var reminder = access.Value;

        var reschedule = DomainGuard.Try(() => reminder.Reschedule(command.DueAt, timeProvider.GetUtcNow()));

        if (reschedule.IsFailure)
        {
            return reschedule.To<Versioned<ReminderDto>>();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReminderDtoMapping.ToVersioned(reminder);
    }
}

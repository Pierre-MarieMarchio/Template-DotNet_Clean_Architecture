using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Services;
using Mediator;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed record CancelReminderCommand(Guid ReminderId, VersionPrecondition? Precondition = null) : ICommand<Result>;

public sealed class CancelReminderCommandHandler(
    IReminderService reminders,
    IUnitOfWork unitOfWork) : ICommandHandler<CancelReminderCommand, Result>
{
    public async ValueTask<Result> Handle(CancelReminderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var access = await reminders.LoadOwnedAsync(command.ReminderId, command.Precondition, cancellationToken);

        if (access.IsFailure)
        {
            return access;
        }

        var cancellation = DomainGuard.Try(access.Value.Cancel);

        if (cancellation.IsFailure)
        {
            return cancellation;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.Reminders.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed class CancelReminderUseCase(
    IReminderService reminders,
    IUnitOfWork unitOfWork,
    IValidator<CancelReminderCommand> validator) : ICancelReminderUseCase
{
    public async Task<Result> ExecuteAsync(CancelReminderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = await validator.EnsureValidAsync(command, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

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

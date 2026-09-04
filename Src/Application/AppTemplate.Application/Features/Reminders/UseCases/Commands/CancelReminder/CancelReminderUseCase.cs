using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Reminders.Services;
using FluentValidation;

namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;

public sealed class CancelReminderUseCase(
    IReminderAccess reminders,
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

        // Caught rather than left to throw: only a reminder that already fired refuses this, and
        // that depends on the aggregate's current state, not on anything the caller could have
        // checked in advance.
        var cancellation = DomainGuard.Try(access.Value.Cancel);

        if (cancellation.IsFailure)
        {
            return cancellation;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

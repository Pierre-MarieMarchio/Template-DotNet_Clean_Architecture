using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Domain.Core.Common.Primitives;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteAccount;

public sealed class DeleteAccountUseCase(
    IAccountAdministrationService administration,
    ISecurityEventLog securityEventLog,
    IDomainEventPublisher domainEvents,
    ICurrentUser currentUser,
    IDateTimeProvider dateTimeProvider,
    IValidator<DeleteAccountCommand> validator) : IDeleteAccountUseCase
{
    public async Task<Result> ExecuteAsync(DeleteAccountCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        var guard = SelfAdministrationPolicy.EnsureNotSelf(
            callerId.Value.Value,
            request.UserId,
            AccountsErrors.CannotDeleteOwnAccount);

        if (guard.IsFailure)
        {
            return guard;
        }

        var outcome = await administration.DeleteAsync(request.UserId, cancellationToken);

        if (outcome.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(outcome.Status));
        }

        securityEventLog.Record(SecurityEvent.AccountDeleted(request.UserId));

        await domainEvents.PublishAsync(
            new UserAccountDeleted(UserId.Create(request.UserId), dateTimeProvider.UtcNow),
            cancellationToken);

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.AccountDeletionRejected,
    };
}

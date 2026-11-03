using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Core.Common.Primitives;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.DeleteAccount;

public sealed record DeleteAccountCommand(Guid UserId) : ICommand<Result>;

public sealed class DeleteAccountCommandHandler(
    IAccountAdministrationService administration,
    ISecurityEventLog securityEventLog,
    IDomainEventPublisher domainEvents,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICommandHandler<DeleteAccountCommand, Result>
{
    public async ValueTask<Result> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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
            new UserAccountDeleted(UserId.Create(request.UserId), timeProvider.GetUtcNow()),
            cancellationToken);

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationStatus status) => status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.AccountDeletionRejected,
    };
}

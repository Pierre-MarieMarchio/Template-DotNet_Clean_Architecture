using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.RemoveRole;

public sealed record RemoveRoleCommand(Guid UserId, string Role) : ICommand<Result>;

public sealed class RemoveRoleCommandHandler(
    IAccountAdministrationService administration,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ICommandHandler<RemoveRoleCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveRoleCommand request, CancellationToken cancellationToken)
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
            AccountsErrors.CannotRemoveOwnRole);

        if (guard.IsFailure)
        {
            return guard;
        }

        var change = await administration.RevokeRoleAsync(request.UserId, request.Role, cancellationToken);

        if (change.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(change));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(sessionTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.RoleRevoked(request.UserId, request.Role));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationOutcome change) => change.Status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.RoleAssignmentRejected(
            change.RejectionMessage ?? "The role could not be revoked."),
    };
}

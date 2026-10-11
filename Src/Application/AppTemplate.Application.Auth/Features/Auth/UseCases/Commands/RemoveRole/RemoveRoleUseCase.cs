using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.RemoveRole;

/// <summary>The self-guard here is not "an administrator may not remove their own
/// <c>Administrator</c> role" — this layer has no reference to that literal, which lives in the
/// persistence module's <c>IdentityRoles</c> spelled exactly once. The guard is broader: an
/// administrator may not remove <em>any</em> role from their own account through this capability,
/// which stays correct even if a derived project seeds a role whose removal is harmless.</summary>
public sealed class RemoveRoleUseCase(
    IAccountAdministrationService administration,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<RemoveRoleCommand> validator) : IRemoveRoleUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(RemoveRoleCommand request, CancellationToken cancellationToken = default)
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
            AuthErrors.CannotRemoveOwnRole);

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
        AccountAdministrationStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.RoleAssignmentRejected(
            change.RejectionMessage ?? "The role could not be revoked."),
    };
}

using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.RemoveRole;

/// <summary>
/// The self-guard here is not "an administrator may not remove their own <c>Administrator</c> role":
/// this layer has no reference to that literal — it lives in the persistence module's
/// <c>IdentityRoles</c>, spelled exactly once so the seeder and the API policy cannot drift apart, and
/// a third spelling here would reopen that. The guard is broader instead: an administrator may not
/// remove <em>any</em> role from their own account through this capability, full stop. Today
/// <c>Admin</c> is the only role seeded, so the practical effect is identical — no administrator can
/// strip their own access — and it stays correct even if a derived project seeds a role whose removal
/// is harmless, since self-service role changes belong on the caller's own account, not behind an
/// administrative endpoint that acts on someone else's.
/// </summary>
public sealed class RemoveRoleUseCase(
    IRoleAssignments roles,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<RemoveRoleCommand> validator) : IRemoveRoleUseCase
{
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

        var guard = SelfAdministrationGuard.EnsureNotSelf(
            callerId.Value,
            request.UserId,
            AuthErrors.CannotRemoveOwnRole);

        if (guard.IsFailure)
        {
            return guard;
        }

        var change = await roles.RemoveRoleAsync(request.UserId, request.Role, cancellationToken);

        if (change.Status is not RoleAssignmentChangeStatus.Applied)
        {
            return Result.Failure(ToError(change));
        }

        await CredentialInvalidation.InvalidateAsync(refreshTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.RoleRevoked(request.UserId, request.Role));

        return Result.Success();
    }

    private static Error ToError(RoleAssignmentChangeOutcome change) => change.Status switch
    {
        RoleAssignmentChangeStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.RoleAssignmentRejected(
            change.RejectionMessage ?? "The role could not be revoked."),
    };
}

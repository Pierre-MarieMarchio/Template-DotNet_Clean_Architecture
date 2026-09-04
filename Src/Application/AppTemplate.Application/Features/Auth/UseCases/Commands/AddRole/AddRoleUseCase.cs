using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.AddRole;

/// <summary>
/// No caller-identity guard, unlike <see cref="RemoveRole.RemoveRoleUseCase"/>: granting a role to
/// oneself cannot strip the access the request is being made under, only add to it.
/// </summary>
public sealed class AddRoleUseCase(
    IRoleAssignmentsService roles,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<AddRoleCommand> validator) : IAddRoleUseCase
{
    public async Task<Result> ExecuteAsync(AddRoleCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var change = await roles.AddRoleAsync(request.UserId, request.Role, cancellationToken);

        if (change.Status is not RoleAssignmentChangeStatus.Applied)
        {
            return Result.Failure(ToError(change));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.RoleGranted(request.UserId, request.Role));

        return Result.Success();
    }

    private static Error ToError(RoleAssignmentChangeOutcome change) => change.Status switch
    {
        RoleAssignmentChangeStatus.NoSuchAccount => AuthErrors.NoSuchAccount,
        _ => AuthErrors.RoleAssignmentRejected(
            change.RejectionMessage ?? "The role could not be granted."),
    };
}

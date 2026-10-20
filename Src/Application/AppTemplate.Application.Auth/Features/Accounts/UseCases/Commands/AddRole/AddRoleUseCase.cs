using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Accounts.UseCases.Commands.AddRole;

public sealed class AddRoleUseCase(
    IAccountAdministrationService administration,
    ISessionTokensService sessionTokens,
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

        var change = await administration.GrantRoleAsync(request.UserId, request.Role, cancellationToken);

        if (change.Status is not AccountAdministrationStatus.Applied)
        {
            return Result.Failure(ToError(change));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(sessionTokens, securityEventLog, request.UserId, cancellationToken);
        securityEventLog.Record(SecurityEvent.RoleGranted(request.UserId, request.Role));

        return Result.Success();
    }

    private static Error ToError(AccountAdministrationOutcome change) => change.Status switch
    {
        AccountAdministrationStatus.NoSuchAccount => AccountsErrors.NoSuchAccount,
        _ => AccountsErrors.RoleAssignmentRejected(
            change.RejectionMessage ?? "The role could not be granted."),
    };
}

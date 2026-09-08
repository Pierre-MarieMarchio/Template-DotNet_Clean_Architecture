using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ChangePassword;

/// <summary>
/// Replaces the password, then invalidates every credential issued under the old one through
/// <c>CredentialInvalidationPolicy</c> — in that order, so nothing is revoked for a change the
/// store refused.
/// </summary>
public sealed class ChangePasswordUseCase(
    IUserAccountsService accounts,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ChangePasswordCommand> validator) : IChangePasswordUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(ChangePasswordCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        var change = await accounts.ChangePasswordAsync(
            userId.Value,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (change.Status is PasswordChangeStatus.IncorrectCurrentPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        if (change.Status is PasswordChangeStatus.Rejected)
        {
            return Result.Failure(
                AuthErrors.RegistrationRejected(
                    change.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value, cancellationToken);

        return Result.Success();
    }
}

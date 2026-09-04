using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ChangePassword;

public sealed class ChangePasswordUseCase(
    IUserAccountsService accounts,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ChangePasswordCommand> validator) : IChangePasswordUseCase
{
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

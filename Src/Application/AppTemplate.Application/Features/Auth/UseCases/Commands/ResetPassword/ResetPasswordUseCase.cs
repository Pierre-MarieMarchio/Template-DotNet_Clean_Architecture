using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ResetPassword;

public sealed class ResetPasswordUseCase(
    IPasswordResetTokens resetTokens,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<ResetPasswordCommand> validator) : IResetPasswordUseCase
{
    public async Task<Result> ExecuteAsync(ResetPasswordCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var reset = await resetTokens.RedeemAsync(
            request.Email,
            request.Token,
            request.NewPassword,
            cancellationToken);

        if (reset.Outcome is PasswordResetOutcome.Rejected)
        {
            return Result.Failure(
                AuthErrors.PasswordResetRejected(
                    reset.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        // An unknown address and an invalid or expired token collapse to the same error, for the
        // reason ConfirmEmailUseCase gives — telling them apart is exactly what a probe wants.
        if (reset.Outcome is not PasswordResetOutcome.Reset || reset.UserId is not { } userId)
        {
            return Result.Failure(AuthErrors.InvalidPasswordReset);
        }

        // The security stamp already rotated inside the store's reset call.
        await CredentialInvalidation.InvalidateAsync(refreshTokens, securityEventLog, userId, cancellationToken);

        return Result.Success();
    }
}

using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ResetPassword;

/// <summary>
/// Redeems the token and sets the password in one step, then invalidates every credential the
/// account already had — the holder of a forgotten password is exactly the case where the live
/// sessions may not be theirs.
/// </summary>
public sealed class ResetPasswordUseCase(
    IPasswordResetTokensService resetTokens,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    IValidator<ResetPasswordCommand> validator) : IResetPasswordUseCase
{
    /// <inheritdoc />
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

        if (reset.Status is PasswordResetStatus.Rejected)
        {
            return Result.Failure(
                AuthErrors.PasswordResetRejected(
                    reset.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        // An unknown address and an invalid or expired token collapse to the same error, for the
        // reason ConfirmEmailUseCase gives — telling them apart is exactly what a probe wants.
        if (reset.Status is not PasswordResetStatus.Reset || reset.UserId is not { } userId)
        {
            return Result.Failure(AuthErrors.InvalidPasswordReset);
        }

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId, cancellationToken);

        return Result.Success();
    }
}

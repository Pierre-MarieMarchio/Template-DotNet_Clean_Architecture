using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Ports;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

/// <summary>Authenticated. The caller proves they still hold the current password before it is replaced.</summary>
public interface IChangePasswordUseCase : IUseCase<ChangePasswordCommand, Result>;

public sealed class ChangePasswordUseCase(
    IUserAccounts accounts,
    IRefreshTokenGrants refreshTokens,
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

        if (change.Outcome is PasswordChangeOutcome.IncorrectCurrentPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        if (change.Outcome is PasswordChangeOutcome.Rejected)
        {
            return Result.Failure(
                AuthErrors.RegistrationRejected(
                    change.RejectionMessage ?? "The submitted password does not meet the required policy."));
        }

        // The security stamp already rotated inside ChangePasswordAsync, which fails every access
        // token in circulation. Refresh tokens survive that rotation, so they are revoked here.
        await refreshTokens.RevokeAllForUserAsync(userId.Value, cancellationToken);
        securityEventLog.Record(SecurityEvent.SecurityStampRotated(userId.Value));

        return Result.Success();
    }
}

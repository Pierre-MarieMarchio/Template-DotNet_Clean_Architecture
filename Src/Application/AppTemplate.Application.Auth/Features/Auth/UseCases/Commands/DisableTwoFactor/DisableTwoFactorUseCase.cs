using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.DisableTwoFactor;

/// <summary>
/// Turns the caller's own second factor off once the password is proven, then revokes every
/// session: dropping a factor is the same posture change as arming one, so the tokens issued
/// under the stronger one do not survive it.
/// </summary>
public sealed class DisableTwoFactorUseCase(
    ITwoFactorEnrollmentService enrollment,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DisableTwoFactorCommand> validator) : IDisableTwoFactorUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(DisableTwoFactorCommand request, CancellationToken cancellationToken = default)
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

        var disabled = await enrollment.DisableAsync(userId.Value.Value, request.CurrentPassword, cancellationToken);

        if (disabled.Status is TwoFactorDisableStatus.IncorrectPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        securityEventLog.Record(SecurityEvent.TwoFactorDisabled(userId.Value.Value));

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value.Value, cancellationToken);

        return Result.Success();
    }
}

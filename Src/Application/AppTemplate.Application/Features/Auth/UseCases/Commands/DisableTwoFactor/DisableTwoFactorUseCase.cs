using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.DisableTwoFactor;

public sealed class DisableTwoFactorUseCase(
    ITwoFactorEnrollmentService enrollment,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<DisableTwoFactorCommand> validator) : IDisableTwoFactorUseCase
{
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

        var disabled = await enrollment.DisableAsync(userId.Value, request.CurrentPassword, cancellationToken);

        if (disabled.Status is TwoFactorDisableStatus.IncorrectPassword)
        {
            return Result.Failure(AuthErrors.IncorrectCurrentPassword);
        }

        securityEventLog.Record(SecurityEvent.TwoFactorDisabled(userId.Value));

        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value, cancellationToken);

        return Result.Success();
    }
}

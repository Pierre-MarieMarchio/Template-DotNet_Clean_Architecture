using AppTemplate.Application.Auth.Features.Auth.Errors;
using AppTemplate.Application.Auth.Features.Auth.Policies;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>
/// Arms two-factor sign-in and hands back the recovery codes, which exist only in this response.
/// Every other session is revoked afterwards, since arming the factor changes what a session is
/// worth.
/// </summary>
public sealed class ConfirmTwoFactorSetupUseCase(
    ITwoFactorEnrollmentService enrollment,
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ConfirmTwoFactorSetupCommand> validator) : IConfirmTwoFactorSetupUseCase
{
    /// <inheritdoc />
    public async Task<Result<ConfirmTwoFactorSetupOutcome>> ExecuteAsync(
        ConfirmTwoFactorSetupCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<ConfirmTwoFactorSetupOutcome>();
        }

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<ConfirmTwoFactorSetupOutcome>();
        }

        var confirmation = await enrollment.ConfirmAsync(
            userId.Value.Value, request.CurrentPassword, request.Code, cancellationToken);

        if (confirmation.Status is TwoFactorConfirmationStatus.IncorrectPassword)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(AuthErrors.IncorrectCurrentPassword);
        }

        if (confirmation.Status is TwoFactorConfirmationStatus.InvalidCode)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(AuthErrors.InvalidTwoFactorCode);
        }

        securityEventLog.Record(SecurityEvent.TwoFactorEnabled(userId.Value.Value));

        // Arming two-factor sign-in is a security-posture change every other session must
        // re-authenticate under.
        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value.Value, cancellationToken);

        return Result.Success(new ConfirmTwoFactorSetupOutcome(confirmation.RecoveryCodes!));
    }
}

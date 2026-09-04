using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.Auth.Errors;
using AppTemplate.Application.Features.Auth.Policies;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.TwoFactorEnrollment;
using FluentValidation;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;

public sealed class ConfirmTwoFactorSetupUseCase(
    ITwoFactorEnrollment enrollment,
    IRefreshTokenGrants refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    IValidator<ConfirmTwoFactorSetupCommand> validator) : IConfirmTwoFactorSetupUseCase
{
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
            userId.Value, request.CurrentPassword, request.Code, cancellationToken);

        if (confirmation.Status is TwoFactorConfirmationStatus.IncorrectPassword)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(AuthErrors.IncorrectCurrentPassword);
        }

        if (confirmation.Status is TwoFactorConfirmationStatus.InvalidCode)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(AuthErrors.InvalidTwoFactorCode);
        }

        securityEventLog.Record(SecurityEvent.TwoFactorEnabled(userId.Value));

        // Arming two-factor sign-in is a security-posture change every other session must
        // re-authenticate under.
        await CredentialInvalidation.InvalidateAsync(refreshTokens, securityEventLog, userId.Value, cancellationToken);

        return Result.Success(new ConfirmTwoFactorSetupOutcome(confirmation.RecoveryCodes!));
    }
}

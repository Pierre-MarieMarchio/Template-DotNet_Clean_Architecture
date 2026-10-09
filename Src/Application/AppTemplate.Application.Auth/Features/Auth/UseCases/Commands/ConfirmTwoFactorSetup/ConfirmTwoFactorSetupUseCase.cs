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
/// Every session is revoked afterwards, the caller's own included, since arming the factor changes
/// what a session is worth; no grant records which session it belongs to, so none can be spared.
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

        if (confirmation.Status is not TwoFactorConfirmationStatus.Confirmed)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(ToError(confirmation.Status));
        }

        securityEventLog.Record(SecurityEvent.TwoFactorEnabled(userId.Value.Value));

        // Arming two-factor sign-in is a security-posture change every session, this one included,
        // must re-authenticate under.
        await CredentialInvalidationPolicy.InvalidateAsync(refreshTokens, securityEventLog, userId.Value.Value, cancellationToken);

        return Result.Success(new ConfirmTwoFactorSetupOutcome(confirmation.RecoveryCodes!));
    }

    private static Error ToError(TwoFactorConfirmationStatus status) => status switch
    {
        TwoFactorConfirmationStatus.IncorrectPassword => AuthErrors.IncorrectCurrentPassword,
        TwoFactorConfirmationStatus.InvalidCode => AuthErrors.InvalidTwoFactorCode,
        TwoFactorConfirmationStatus.AlreadyEnabled => AuthErrors.TwoFactorAlreadyEnabled,

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        TwoFactorConfirmationStatus.NoSuchAccount => CommonErrors.NotAuthenticated,
        _ => AuthErrors.TwoFactorEnableRejected,
    };
}

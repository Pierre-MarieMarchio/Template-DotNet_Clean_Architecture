using AppTemplate.Application.Auth.Features.Accounts.Model.Errors;
using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Model.Errors;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;

/// <summary>
/// Arms two-factor sign-in and hands back the recovery codes, which exist only in this response.
/// Every other session is revoked afterwards, since arming the factor changes what a session is
/// worth; the caller's own is spared, having just proved both the password and the new factor. A
/// caller whose token names no session loses every session, its own included.
/// </summary>
public sealed class ConfirmTwoFactorSetupUseCase(
    ITwoFactorService twoFactor,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession,
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

        var confirmation = await twoFactor.ConfirmSetupAsync(
            userId.Value.Value, request.CurrentPassword, request.Code, cancellationToken);

        if (confirmation.Status is not TwoFactorConfirmationStatus.Confirmed)
        {
            return Result.Failure<ConfirmTwoFactorSetupOutcome>(ToError(confirmation.Status));
        }

        securityEventLog.Record(SecurityEvent.TwoFactorEnabled(userId.Value.Value));

        // Arming two-factor sign-in is a security-posture change every other session must
        // re-authenticate under.
        await CredentialInvalidationPolicy.InvalidateOtherSessionsAsync(
            sessionTokens,
            securityEventLog,
            userId.Value.Value,
            currentSession.SessionId,
            cancellationToken);

        return Result.Success(new ConfirmTwoFactorSetupOutcome(confirmation.RecoveryCodes!));
    }

    private static Error ToError(TwoFactorConfirmationStatus status) => status switch
    {
        TwoFactorConfirmationStatus.IncorrectPassword => AccountsErrors.IncorrectCurrentPassword,
        TwoFactorConfirmationStatus.InvalidCode => TwoFactorErrors.InvalidTwoFactorCode,
        TwoFactorConfirmationStatus.AlreadyEnabled => TwoFactorErrors.TwoFactorAlreadyEnabled,

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        TwoFactorConfirmationStatus.NoSuchAccount => CommonErrors.NotAuthenticated,
        _ => TwoFactorErrors.TwoFactorEnableRejected,
    };
}

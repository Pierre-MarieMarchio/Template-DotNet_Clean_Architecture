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

namespace AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.DisableTwoFactor;

/// <summary>
/// Turns the caller's own second factor off once the password is proven, then revokes every other
/// session: dropping a factor is the same posture change as arming one, so the tokens issued
/// under the stronger one do not survive it. The caller's own session, which just proved the
/// password, is spared; a token naming no session spares nothing.
/// </summary>
public sealed class DisableTwoFactorUseCase(
    ITwoFactorService twoFactor,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession,
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

        var disabled = await twoFactor.DisableAsync(userId.Value.Value, request.CurrentPassword, cancellationToken);

        // A token that outlived its account, answered as GetCurrentUserUseCase answers it.
        if (disabled is TwoFactorDisableStatus.NoSuchAccount)
        {
            return Result.Failure(CommonErrors.NotAuthenticated);
        }

        if (disabled is TwoFactorDisableStatus.IncorrectPassword)
        {
            return Result.Failure(AccountsErrors.IncorrectCurrentPassword);
        }

        // Nothing changed, so there is no posture change to record or revoke for.
        if (disabled is TwoFactorDisableStatus.Rejected)
        {
            return Result.Failure(TwoFactorErrors.TwoFactorDisableRejected);
        }

        securityEventLog.Record(SecurityEvent.TwoFactorDisabled(userId.Value.Value));

        await CredentialInvalidationPolicy.InvalidateOtherSessionsAsync(
            sessionTokens,
            securityEventLog,
            userId.Value.Value,
            currentSession.SessionId,
            cancellationToken);

        return Result.Success();
    }
}

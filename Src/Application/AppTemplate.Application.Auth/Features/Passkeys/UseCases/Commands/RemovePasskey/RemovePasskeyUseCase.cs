using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

/// <summary>
/// Removes one of the caller's own passkeys. Somebody else's passkey, an unknown id and one
/// already removed are the same 404, so the route cannot be used to learn which credential ids
/// exist.
/// <para>
/// A removal is a credential change, and it ends every other session as a password change does:
/// a session opened with the passkey just withdrawn — a stolen authenticator's, say — must not
/// outlive it. The adapter has already rotated the security stamp, which takes every access token
/// with it; the refresh tokens are revoked here through <c>CredentialInvalidationPolicy</c>. The
/// session the removal is made from is spared and refreshes into the new posture; a token naming
/// no session spares nothing. Nothing is revoked for a removal the store refused.
/// </para>
/// </summary>
public sealed class RemovePasskeyUseCase(
    IPasskeyService passkeys,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession,
    IValidator<RemovePasskeyCommand> validator) : IRemovePasskeyUseCase
{
    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(RemovePasskeyCommand request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = await validator.EnsureValidAsync(request, cancellationToken);

        if (validation.IsFailure)
        {
            return validation;
        }

        var callerId = currentUser.RequireUserId();

        if (callerId.IsFailure)
        {
            return callerId;
        }

        if (!await passkeys.RemoveAsync(callerId.Value.Value, request.CredentialId, cancellationToken))
        {
            return Result.Failure(PasskeysErrors.NoSuchPasskey);
        }

        securityEventLog.Record(SecurityEvent.PasskeyRemoved(callerId.Value.Value));

        await CredentialInvalidationPolicy.InvalidateOtherSessionsAsync(
            sessionTokens,
            securityEventLog,
            callerId.Value.Value,
            currentSession.SessionId,
            cancellationToken);

        return Result.Success();
    }
}

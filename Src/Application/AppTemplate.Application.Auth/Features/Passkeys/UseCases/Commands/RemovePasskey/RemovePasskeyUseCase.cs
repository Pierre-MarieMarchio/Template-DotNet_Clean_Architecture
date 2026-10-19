using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using FluentValidation;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

/// <summary>
/// Removes one of the caller's own passkeys. Somebody else's passkey and an unknown id are the
/// same 404, so the route cannot be used to learn which credential ids exist. A passkey already
/// removed succeeds again: the outcome the caller asked for holds either way.
/// <para>
/// What removing a credential owes the sessions is nothing: the sessions hold tokens, not
/// credentials, and the account's other ways in are untouched. The audit trail gets the fact; a
/// holder locking themselves out is what the other ways in — and the password reset — exist for.
/// </para>
/// </summary>
public sealed class RemovePasskeyUseCase(
    IPasskeyService passkeys,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
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

        return Result.Success();
    }
}

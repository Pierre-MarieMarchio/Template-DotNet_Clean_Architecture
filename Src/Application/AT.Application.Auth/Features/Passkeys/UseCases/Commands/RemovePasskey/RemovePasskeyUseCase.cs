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

public sealed class RemovePasskeyUseCase(
    IPasskeyService passkeys,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession,
    IValidator<RemovePasskeyCommand> validator) : IRemovePasskeyUseCase
{
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

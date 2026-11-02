using AppTemplate.Application.Auth.Features.Accounts.Model.Policies;
using AppTemplate.Application.Auth.Features.Passkeys.Model.Errors;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Passkeys.UseCases.Commands.RemovePasskey;

public sealed record RemovePasskeyCommand(byte[] CredentialId) : ICommand<Result>;

public sealed class RemovePasskeyCommandHandler(
    IPasskeyService passkeys,
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser,
    ICurrentSession currentSession) : ICommandHandler<RemovePasskeyCommand, Result>
{
    public async ValueTask<Result> Handle(RemovePasskeyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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

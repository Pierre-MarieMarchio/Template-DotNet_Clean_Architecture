using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RevokeSession;

public sealed record RevokeSessionCommand(Guid SessionId) : ICommand<Result>;

public sealed class RevokeSessionCommandHandler(
    ISessionsService sessions,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ICommandHandler<RevokeSessionCommand, Result>
{
    public async ValueTask<Result> Handle(RevokeSessionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        if (!await sessions.RevokeAsync(userId.Value.Value, request.SessionId, cancellationToken))
        {
            return Result.Failure(SessionsErrors.NoSuchSession);
        }

        await securityEventLog.RecordAsync(SecurityEvent.RefreshTokenRevoked(userId.Value.Value));

        return Result.Success();
    }
}

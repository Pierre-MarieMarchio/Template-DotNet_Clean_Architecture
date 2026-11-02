using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.LogoutEverywhere;

public sealed record LogoutEverywhereCommand : ICommand<Result>;

public sealed class LogoutEverywhereCommandHandler(
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ICommandHandler<LogoutEverywhereCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutEverywhereCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId;
        }

        await sessionTokens.RevokeAllRefreshTokensAsync(userId.Value.Value, cancellationToken: cancellationToken);
        securityEventLog.Record(SecurityEvent.RefreshTokenRevoked(userId.Value.Value));

        return Result.Success();
    }
}

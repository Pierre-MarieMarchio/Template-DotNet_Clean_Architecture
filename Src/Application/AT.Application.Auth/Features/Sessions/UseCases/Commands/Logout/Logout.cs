using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Results;
using Mediator;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Result>;

public sealed class LogoutCommandHandler(
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog) : ICommandHandler<LogoutCommand, Result>
{
    public async ValueTask<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userId = await sessionTokens.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);

        if (userId is { } id)
        {
            securityEventLog.Record(SecurityEvent.LoggedOut(id));
        }

        return Result.Success();
    }
}

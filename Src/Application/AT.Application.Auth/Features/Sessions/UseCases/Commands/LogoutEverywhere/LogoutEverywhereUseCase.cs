using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.LogoutEverywhere;

public sealed class LogoutEverywhereUseCase(
    ISessionTokensService sessionTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ILogoutEverywhereUseCase
{
    public async Task<Result> ExecuteAsync(CancellationToken cancellationToken = default)
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

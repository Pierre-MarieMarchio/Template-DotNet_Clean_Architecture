using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands.LogoutEverywhere;

public sealed class LogoutEverywhereUseCase(
    IRefreshTokenGrantsService refreshTokens,
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

        // Revocation only: rotating the security stamp here would also kill the access token the
        // caller just used to ask for this, signing out the very session that made the request.
        await refreshTokens.RevokeAllForUserAsync(userId.Value, cancellationToken);
        securityEventLog.Record(SecurityEvent.RefreshTokenRevoked(userId.Value));

        return Result.Success();
    }
}

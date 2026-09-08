using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.LogoutEverywhere;

/// <summary>
/// Revokes every grant the caller holds, this request's own included: the access token in hand
/// keeps working until it expires, but nothing can be minted against the account again without a
/// fresh sign-in.
/// </summary>
public sealed class LogoutEverywhereUseCase(
    IRefreshTokenGrantsService refreshTokens,
    ISecurityEventLog securityEventLog,
    ICurrentUser currentUser) : ILogoutEverywhereUseCase
{
    /// <inheritdoc />
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

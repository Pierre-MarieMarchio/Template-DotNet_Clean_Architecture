using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;

namespace AppTemplate.Application.Features.Auth.UseCases.Commands;

/// <summary>Whole input is ambient: the caller's own id, taken from the request's principal.</summary>
public interface ILogoutEverywhereUseCase : IUseCase<Result>;

public sealed class LogoutEverywhereUseCase(
    IRefreshTokenGrants refreshTokens,
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

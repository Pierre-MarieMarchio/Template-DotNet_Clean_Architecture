namespace AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

public interface ISessionTokensService
{
    Task<IssuedAccessToken> IssueAccessTokenAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId,
        SessionClient client,
        CancellationToken cancellationToken = default);

    Task<RefreshTokenRotation> RotateRefreshTokenAsync(
        string presentedToken,
        SessionClient client,
        CancellationToken cancellationToken = default);

    Task<Guid?> RevokeRefreshTokenAsync(string presentedToken, CancellationToken cancellationToken = default);

    Task RevokeAllRefreshTokensAsync(
        Guid userId,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

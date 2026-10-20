namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;

public interface IRefreshTokenTable
{
    void Add(Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt);

    Task<RefreshTokenGrant?> FindAsync(string tokenHash, CancellationToken cancellationToken = default);

    Task<bool> TryRotateAsync(
        string presentedTokenHash,
        string replacementTokenHash,
        DateTimeOffset now,
        DateTimeOffset replacementExpiresAt,
        CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

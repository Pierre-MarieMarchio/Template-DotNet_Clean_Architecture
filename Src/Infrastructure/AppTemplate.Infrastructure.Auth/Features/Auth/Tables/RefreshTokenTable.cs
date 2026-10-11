using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Tables;

/// <summary>
/// Reads and writes refresh-token grant rows. Internal: the identity module depends on
/// <see cref="IRefreshTokenTable"/> and never on this class or on the row type it handles.
/// </summary>
internal sealed class RefreshTokenTable(AuthDbContext context) : IRefreshTokenTable
{
    public void Add(Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        context.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = tokenHash,
            UserId = userId,
            FamilyId = familyId,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        });
    }

    public async Task<RefreshTokenGrant?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        // Projected rather than materialised: the caller needs the grant's state and its session's,
        // and handing it a tracked row would let it write one without going through the rules in
        // this interface.
        return await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new RefreshTokenGrant(
                token.UserId,
                token.FamilyId,
                token.ExpiresAt,
                token.RevokedAt,
                token.Family!.RevokedAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryRotateAsync(
        string presentedTokenHash,
        string replacementTokenHash,
        DateTimeOffset now,
        DateTimeOffset replacementExpiresAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presentedTokenHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementTokenHash);

        // One statement, and the liveness test is inside it. The database therefore picks the winner
        // between two simultaneous presentations and reports the outcome as the affected-row count.
        // Nothing about the decision depends on what a preceding read saw.
        int consumed = await context.RefreshTokens
            .Where(token => token.TokenHash == presentedTokenHash && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.ReplacedByTokenHash, replacementTokenHash),
                cancellationToken);

        if (consumed == 0)
        {
            return false;
        }

        // Read back rather than taken from the caller, so the successor cannot be attached to a
        // user or a session other than the ones the consumed grant belonged to. The row exists: it
        // was just updated.
        var grant = await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == presentedTokenHash)
            .Select(token => new { token.UserId, token.FamilyId })
            .FirstAsync(cancellationToken);

        Add(grant.UserId, grant.FamilyId, replacementTokenHash, now, replacementExpiresAt);

        return true;
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        await context.RefreshTokens
            .Where(token => token.ExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}

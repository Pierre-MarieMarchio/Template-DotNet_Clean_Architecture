using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;

/// <summary>
/// Reads and writes refresh-token grant rows. Internal: the identity module depends on
/// <see cref="IRefreshTokenStore"/> and never on this class or on the row type it handles.
/// </summary>
internal sealed class RefreshTokenStore(AppDbContext context) : IRefreshTokenStore
{
    public void Add(Guid userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        context.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = tokenHash,
            UserId = userId,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        });
    }

    public async Task<RefreshTokenGrant?> FindAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        // Projected rather than materialised: the caller needs three values, and handing it a tracked
        // row would let it write one without going through the rules in this interface.
        return await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => new RefreshTokenGrant(token.UserId, token.ExpiresAt, token.RevokedAt))
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
        // between two simultaneous presentations and reports the outcome as the affected-row count;
        // nothing about the decision depends on what a preceding read saw.
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

        // Read back rather than taken from the caller, so the successor cannot be attached to a user
        // other than the one the consumed grant belonged to. The row exists: it was just updated.
        var userId = await context.RefreshTokens
            .AsNoTracking()
            .Where(token => token.TokenHash == presentedTokenHash)
            .Select(token => token.UserId)
            .FirstAsync(cancellationToken);

        Add(userId, replacementTokenHash, now, replacementExpiresAt);

        return true;
    }

    public async Task RevokeAsync(
        string tokenHash,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return;
        }

        var stored = await context.RefreshTokens
            .FirstOrDefaultAsync(
                token => token.TokenHash == tokenHash && token.RevokedAt == null,
                cancellationToken);

        stored?.RevokedAt = revokedAt;
    }

    public async Task RevokeAllForUserAsync(
        Guid userId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        var active = await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAt = revokedAt;
        }
    }
}

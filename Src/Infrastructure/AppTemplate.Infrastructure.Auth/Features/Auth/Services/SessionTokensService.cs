using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Issuers;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Tables;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Services;

/// <summary><see cref="ISessionTokensService"/>: signs access tokens through
/// <see cref="AccessTokenIssuer"/>, and issues, rotates, revokes and purges refresh tokens, one
/// family per session. A refresh token is 32 CSPRNG bytes, base64url-encoded — not a JWT, so a
/// stolen one is not also a bearer token; only its SHA-256 hash is persisted, and every presentation
/// rotates the grant — presenting one already rotated means replay or leak, so every session of that
/// user is revoked, while presenting one of an ended session is merely refused, without the alarm.
/// Rows are staged through <see cref="IRefreshTokenTable"/> and
/// <see cref="IRefreshTokenFamilyTable"/> and committed through
/// <see cref="IContextUnitOfWork{TContext}"/>, so a rotation could share a transaction with other
/// work; consuming a presented grant is the one step that does not wait — single-use rotation is
/// decided by one conditional UPDATE (<see cref="IRefreshTokenTable.TryRotateAsync"/> says why).</summary>
internal sealed class SessionTokensService(
    AccessTokenIssuer accessTokenIssuer,
    IRefreshTokenTable table,
    IRefreshTokenFamilyTable families,
    IAppUserDirectory directory,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IOptions<RefreshTokenOptions> options,
    ISecurityEventLog securityEventLog,
    ILogger<SessionTokensService> logger) : ISessionTokensService
{
    /// <summary>256 bits of entropy: the token is the credential, so it must not be guessable.</summary>
    private const int _tokenSizeInBytes = 32;

    /// <summary>
    /// A plain SHA-256, deliberately: unlike a password this is high-entropy random data, so there
    /// is nothing to brute-force and a deliberately slow KDF would only add latency to every refresh.
    /// </summary>
    internal static string ComputeHash(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public Task<IssuedAccessToken> IssueAccessTokenAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        accessTokenIssuer.IssueAsync(userId, sessionId, cancellationToken);

    public async Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId,
        SessionClient client,
        CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;
        var (value, expiresAt) = CreateSecret(now);

        // The family first, so the grant is staged into a session the context already knows about.
        Guid familyId = families.Start(userId, now, client.UserAgent);
        table.Add(userId, familyId, ComputeHash(value), now, expiresAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(value, expiresAt, familyId);
    }

    public async Task<RefreshTokenRotation> RotateRefreshTokenAsync(
        string presentedToken,
        SessionClient client,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return RefreshTokenRotation.Rejected;
        }

        string presentedHash = ComputeHash(presentedToken);
        var grant = await table.FindAsync(presentedHash, cancellationToken);

        if (grant is null)
        {
            return RefreshTokenRotation.Rejected;
        }

        // A session that was ended — signed out, revoked, or killed with the rest — refuses quietly:
        // the client still holding its token has done nothing suspicious, so nothing is revoked
        // beyond what already was. This precedes the replay test, because a grant revoked by its
        // session's ending is not evidence of one.
        if (grant.FamilyRevokedAt is not null)
        {
            return RefreshTokenRotation.Rejected;
        }

        if (grant.RevokedAt is not null)
        {
            return await RespondToReplayAsync(grant.UserId, cancellationToken);
        }

        if (!grant.IsActiveAt(dateTimeProvider.UtcNow))
        {
            return RefreshTokenRotation.Rejected;
        }

        // A grant outliving its account is not a grant.
        var user = await directory.FindByIdAsync(grant.UserId, cancellationToken);

        if (user is null)
        {
            return RefreshTokenRotation.Rejected;
        }

        var (replacementValue, replacementExpiresAt) = CreateSecret(dateTimeProvider.UtcNow);

        // The table consumes the grant with a single conditional UPDATE, so of two simultaneous
        // presentations exactly one is told it rotated. The grant was live when it was read a moment
        // ago, so the only way to be refused here is that something else consumed it in between —
        // which is indistinguishable from a stolen copy being redeemed, and is answered as one.
        bool rotated = await table.TryRotateAsync(
            presentedHash,
            ComputeHash(replacementValue),
            dateTimeProvider.UtcNow,
            replacementExpiresAt,
            cancellationToken);

        if (!rotated)
        {
            return await RespondToReplayAsync(grant.UserId, cancellationToken);
        }

        // A session ended between the read and this write gets its successor — and a successor is
        // all it gets, because MarkUsedAsync and every later presentation test the family's
        // revocation. That is how ending a session wins its race with a rotation in flight.
        await families.MarkUsedAsync(grant.FamilyId, client.UserAgent, dateTimeProvider.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RefreshTokenRotation.Rotated(
            user.Id,
            new IssuedRefreshToken(replacementValue, replacementExpiresAt, grant.FamilyId));
    }

    public async Task<Guid?> RevokeRefreshTokenAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return null;
        }

        var grant = await table.FindAsync(ComputeHash(presentedToken), cancellationToken);

        if (grant is null)
        {
            return null;
        }

        // Signing out ends the session, not the one grant the caller happens to present: the
        // session is the unit the owner lists and ends.
        await families.EndAsync(grant.UserId, grant.FamilyId, dateTimeProvider.UtcNow, cancellationToken);

        return grant.UserId;
    }

    public async Task RevokeAllRefreshTokensAsync(
        Guid userId,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        await families.RevokeAllForUserAsync(userId, exceptSessionId, dateTimeProvider.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        int purged = await table.PurgeExpiredAsync(asOf.AddDays(-options.Value.RetentionInDays), cancellationToken);
        await families.PurgeChildlessAsync(cancellationToken);

        return purged;
    }

    /// <summary>
    /// A grant presented after it was consumed. Either the legitimate holder replayed it or somebody
    /// else has a copy; both mean the chain can no longer be trusted.
    /// </summary>
    private async Task<RefreshTokenRotation> RespondToReplayAsync(Guid userId, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "A consumed refresh token was presented for user {UserId}. Revoking every session of the user.",
            userId);

        securityEventLog.Record(SecurityEvent.RefreshTokenReplayDetected(userId));

        await RevokeAllRefreshTokensAsync(userId, cancellationToken: cancellationToken);

        return RefreshTokenRotation.Rejected;
    }

    private (string Value, DateTimeOffset ExpiresAt) CreateSecret(DateTimeOffset now) =>
        (Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_tokenSizeInBytes)),
            now.AddDays(options.Value.LifetimeInDays));
}

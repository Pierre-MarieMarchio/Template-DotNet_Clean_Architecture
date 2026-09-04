using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Infrastructure.Identity.Common.Directories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Services;

/// <summary>
/// Issues, rotates and revokes refresh tokens.
///
/// A refresh token is 32 bytes from a CSPRNG, base64url-encoded — not a JWT, so a stolen refresh
/// token is not also a bearer token. Only the SHA-256 hash is persisted, and every presentation
/// rotates the grant: presenting one that was already rotated or revoked means either a replay or a
/// leak, so the entire family for that user is revoked and the request fails.
///
/// <para>
/// Rows are staged through <see cref="IRefreshTokenTable"/> and committed through
/// <see cref="IUnitOfWork"/> — the same unit of work a domain write uses — so a rotation could share
/// a transaction with other work. The one step that does not wait for that commit is consuming a
/// presented grant: single-use rotation has to be decided by one conditional UPDATE, and
/// <see cref="IRefreshTokenTable.TryRotateAsync"/> says why.
/// </para>
/// </summary>
internal sealed class RefreshTokenGrantsService(
    IRefreshTokenTable table,
    IAppUserDirectory directory,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IOptions<RefreshTokenOptions> options,
    ISecurityEventLog securityEventLog,
    ILogger<RefreshTokenGrantsService> logger) : IRefreshTokenGrantsService
{
    /// <summary>256 bits of entropy: the token is the credential, so it must not be guessable.</summary>
    private const int _tokenSizeInBytes = 32;

    /// <summary>
    /// A plain SHA-256, deliberately: unlike a password this is high-entropy random data, so there
    /// is nothing to brute-force and a deliberately slow KDF would only add latency to every refresh.
    /// </summary>
    internal static string ComputeHash(string token) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;
        var (value, expiresAt) = CreateSecret(now);

        table.Add(userId, ComputeHash(value), now, expiresAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(value, expiresAt);
    }

    public async Task<RefreshTokenRotation> RotateAsync(
        string presentedToken,
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

        var now = dateTimeProvider.UtcNow;

        if (grant.RevokedAt is not null)
        {
            return await RespondToReplayAsync(grant.UserId, cancellationToken);
        }

        if (!grant.IsActiveAt(now))
        {
            return RefreshTokenRotation.Rejected;
        }

        // A grant outliving its account is not a grant.
        var user = await directory.FindByIdAsync(grant.UserId, cancellationToken);

        if (user is null)
        {
            return RefreshTokenRotation.Rejected;
        }

        var (replacementValue, replacementExpiresAt) = CreateSecret(now);

        // The table consumes the grant with a single conditional UPDATE, so of two simultaneous
        // presentations exactly one is told it rotated. The grant was live when it was read a moment
        // ago, so the only way to be refused here is that something else consumed it in between —
        // which is indistinguishable from a stolen copy being redeemed, and is answered as one.
        bool rotated = await table.TryRotateAsync(
            presentedHash,
            ComputeHash(replacementValue),
            now,
            replacementExpiresAt,
            cancellationToken);

        if (!rotated)
        {
            return await RespondToReplayAsync(grant.UserId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RefreshTokenRotation.Rotated(
            user.Id,
            new IssuedRefreshToken(replacementValue, replacementExpiresAt));
    }

    public async Task<Guid?> RevokeAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return null;
        }

        string hash = ComputeHash(presentedToken);
        var grant = await table.FindAsync(hash, cancellationToken);

        if (grant is null)
        {
            return null;
        }

        await table.RevokeAsync(hash, dateTimeProvider.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return grant.UserId;
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await table.RevokeAllForUserAsync(userId, dateTimeProvider.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A grant presented after it was consumed. Either the legitimate holder replayed it or somebody
    /// else has a copy; both mean the chain can no longer be trusted.
    /// </summary>
    private async Task<RefreshTokenRotation> RespondToReplayAsync(Guid userId, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "A consumed refresh token was presented for user {UserId}. Revoking the whole token family.",
            userId);

        securityEventLog.Record(SecurityEvent.RefreshTokenReplayDetected(userId));

        await RevokeAllForUserAsync(userId, cancellationToken);

        return RefreshTokenRotation.Rejected;
    }

    private (string Value, DateTimeOffset ExpiresAt) CreateSecret(DateTimeOffset now) =>
        (Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_tokenSizeInBytes)),
            now.AddDays(options.Value.LifetimeInDays));
}

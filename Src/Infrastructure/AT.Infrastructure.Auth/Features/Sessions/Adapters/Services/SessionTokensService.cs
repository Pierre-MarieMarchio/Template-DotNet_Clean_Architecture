using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;

internal sealed class SessionTokensService(
    AccessTokenIssuer accessTokenIssuer,
    IRefreshTokenTable table,
    IRefreshTokenFamilyTable families,
    IAppUserDirectory directory,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    AuthDbContext context,
    IDateTimeProvider dateTimeProvider,
    IOptions<RefreshTokenOptions> options,
    ISecurityEventLog securityEventLog,
    ILogger<SessionTokensService> logger) : ISessionTokensService
{
    private const int _tokenSizeInBytes = 32;

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

        var user = await directory.FindByIdAsync(grant.UserId, cancellationToken);

        if (user is null)
        {
            return RefreshTokenRotation.Rejected;
        }

        var (replacementValue, replacementExpiresAt) = CreateSecret(dateTimeProvider.UtcNow);
        string replacementHash = ComputeHash(replacementValue);

        bool rotated = await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
            async ct => await RotateAsync(presentedHash, replacementHash, replacementExpiresAt, grant.FamilyId, client, ct),
            async ct => await table.FindAsync(replacementHash, ct) is not null,
            cancellationToken);

        if (!rotated)
        {
            return await RespondToReplayAsync(grant.UserId, cancellationToken);
        }

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

    private async Task<bool> RotateAsync(
        string presentedHash,
        string replacementHash,
        DateTimeOffset replacementExpiresAt,
        Guid familyId,
        SessionClient client,
        CancellationToken cancellationToken)
    {
        if (!await table.TryRotateAsync(
            presentedHash,
            replacementHash,
            dateTimeProvider.UtcNow,
            replacementExpiresAt,
            cancellationToken))
        {
            return false;
        }

        await families.MarkUsedAsync(familyId, client.UserAgent, dateTimeProvider.UtcNow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

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

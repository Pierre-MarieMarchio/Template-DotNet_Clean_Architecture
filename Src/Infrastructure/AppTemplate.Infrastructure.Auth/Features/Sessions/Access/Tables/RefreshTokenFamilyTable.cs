using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;

internal sealed class RefreshTokenFamilyTable(AuthDbContext context) : IRefreshTokenFamilyTable
{
    public Guid Start(Guid userId, DateTimeOffset createdAt, string? userAgent)
    {
        var family = new RefreshTokenFamilyRecord
        {
            UserId = userId,
            CreatedAt = createdAt,
            LastUsedAt = createdAt,
            UserAgent = Shorten(userAgent),
        };

        context.RefreshTokenFamilies.Add(family);

        return family.Id;
    }

    public async Task MarkUsedAsync(
        Guid familyId,
        string? userAgent,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await context.RefreshTokenFamilies
            .Where(family => family.Id == familyId && family.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(family => family.LastUsedAt, now)
                    .SetProperty(family => family.UserAgent, Shorten(userAgent)),
                cancellationToken);
    }

    public async Task<bool> EndAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        bool exists = await context.RefreshTokenFamilies
            .AsNoTracking()
            .AnyAsync(family => family.UserId == userId && family.Id == familyId, cancellationToken);

        if (!exists)
        {
            return false;
        }

        await context.RefreshTokenFamilies
            .Where(family => family.Id == familyId && family.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(family => family.RevokedAt, revokedAt),
                cancellationToken);

        await context.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, revokedAt),
                cancellationToken);

        return true;
    }

    public async Task RevokeAllForUserAsync(
        Guid userId,
        Guid? exceptFamilyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default)
    {
        await context.RefreshTokenFamilies
            .Where(family => family.UserId == userId
                && family.RevokedAt == null
                && (exceptFamilyId == null || family.Id != exceptFamilyId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(family => family.RevokedAt, revokedAt),
                cancellationToken);

        await context.RefreshTokens
            .Where(token => token.UserId == userId
                && token.RevokedAt == null
                && (exceptFamilyId == null || token.FamilyId != exceptFamilyId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, revokedAt),
                cancellationToken);
    }

    public async Task<IReadOnlyList<ActiveSession>> ListLiveAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var sessions = await context.RefreshTokenFamilies
            .AsNoTracking()
            .Where(family => family.UserId == userId && family.RevokedAt == null)
            .Where(family => context.RefreshTokens.Any(token =>
                token.FamilyId == family.Id
                && token.RevokedAt == null
                && token.ExpiresAt > asOf))
            .OrderByDescending(family => family.LastUsedAt)
            .Select(family => new ActiveSession(
                family.Id,
                family.CreatedAt,
                family.LastUsedAt,
                family.UserAgent))
            .ToListAsync(cancellationToken);

        return sessions;
    }

    public async Task<int> PurgeChildlessAsync(CancellationToken cancellationToken = default) =>
        await context.RefreshTokenFamilies
            .Where(family => !context.RefreshTokens.Any(token => token.FamilyId == family.Id))
            .ExecuteDeleteAsync(cancellationToken);

    private static string? Shorten(string? userAgent) =>
        userAgent is null
            ? null
            : userAgent.Length <= RefreshTokenFamilyConfiguration.UserAgentLength
                ? userAgent
                : userAgent[..RefreshTokenFamilyConfiguration.UserAgentLength];
}

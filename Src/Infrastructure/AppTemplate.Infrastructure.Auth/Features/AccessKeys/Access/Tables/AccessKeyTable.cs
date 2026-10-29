using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Rows.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Auth.Features.AccessKeys.Access.Tables;

internal sealed class AccessKeyTable(AuthDbContext context) : IAccessKeyTable
{
    public Guid Add(
        Guid userId,
        string name,
        string keyHash,
        string prefix,
        IReadOnlyList<string> scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(scopes);

        var accessKey = new AccessKeyRecord
        {
            UserId = userId,
            Name = name,
            KeyHash = keyHash,
            Prefix = prefix,
            Scopes = string.Join(AccessKeyConfiguration.ScopeSeparator, scopes),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
        };

        context.AccessKeys.Add(accessKey);

        return accessKey.Id;
    }

    public async Task<int> CountLiveAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default) =>
        await LiveKeysOf(userId, asOf).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<LiveAccessKey>> ListLiveAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var rows = await LiveKeysOf(userId, asOf)
            .AsNoTracking()
            .OrderByDescending(accessKey => accessKey.CreatedAt)
            .ThenByDescending(accessKey => accessKey.Id)
            .Select(accessKey => new
            {
                accessKey.Id,
                accessKey.Name,
                accessKey.Prefix,
                accessKey.Scopes,
                accessKey.CreatedAt,
                accessKey.LastUsedAt,
                accessKey.ExpiresAt,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new LiveAccessKey(
                row.Id,
                row.Name,
                row.Prefix,
                ScopesOf(row.Scopes),
                row.CreatedAt,
                row.LastUsedAt,
                row.ExpiresAt)),
        ];
    }

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid accessKeyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default) =>
        await LiveKeysOf(userId, revokedAt)
            .Where(accessKey => accessKey.Id == accessKeyId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(accessKey => accessKey.RevokedAt, revokedAt),
                cancellationToken) > 0;

    public async Task<AccessKeyGrant?> FindAsync(string keyHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);

        var row = await context.AccessKeys
            .AsNoTracking()
            .Where(accessKey => accessKey.KeyHash == keyHash)
            .Select(accessKey => new
            {
                accessKey.Id,
                accessKey.UserId,
                accessKey.Scopes,
                accessKey.ExpiresAt,
                accessKey.RevokedAt,
                accessKey.LastUsedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new AccessKeyGrant(
                row.Id,
                row.UserId,
                ScopesOf(row.Scopes),
                row.ExpiresAt,
                row.RevokedAt,
                row.LastUsedAt);
    }

    public async Task MarkUsedAsync(
        Guid accessKeyId,
        DateTimeOffset usedAt,
        DateTimeOffset unlessUsedAfter,
        CancellationToken cancellationToken = default) =>
        await context.AccessKeys
            .Where(accessKey => accessKey.Id == accessKeyId
                && (accessKey.LastUsedAt == null || accessKey.LastUsedAt <= unlessUsedAfter))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(accessKey => accessKey.LastUsedAt, usedAt),
                cancellationToken);

    private IQueryable<AccessKeyRecord> LiveKeysOf(Guid userId, DateTimeOffset asOf) =>
        context.AccessKeys.Where(accessKey => accessKey.UserId == userId
            && accessKey.RevokedAt == null
            && (accessKey.ExpiresAt == null || accessKey.ExpiresAt > asOf));

    private static string[] ScopesOf(string scopes) =>
        scopes.Split(AccessKeyConfiguration.ScopeSeparator, StringSplitOptions.RemoveEmptyEntries);
}

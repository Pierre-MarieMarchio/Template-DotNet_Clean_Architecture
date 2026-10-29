using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;

namespace AppTemplate.Infrastructure.Auth.Features.AccessKeys.Access.Tables;

internal interface IAccessKeyTable
{
    Guid Add(
        Guid userId,
        string name,
        string keyHash,
        string prefix,
        IReadOnlyList<string> scopes,
        DateTimeOffset createdAt,
        DateTimeOffset? expiresAt);

    Task<int> CountLiveAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveAccessKey>> ListLiveAsync(
        Guid userId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(
        Guid userId,
        Guid accessKeyId,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken = default);

    Task<AccessKeyGrant?> FindAsync(string keyHash, CancellationToken cancellationToken = default);

    Task MarkUsedAsync(
        Guid accessKeyId,
        DateTimeOffset usedAt,
        DateTimeOffset unlessUsedAfter,
        CancellationToken cancellationToken = default);
}

namespace AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;

public interface IAccessKeysService
{
    Task<IssuedAccessKey> IssueAsync(
        Guid userId,
        string name,
        IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default);

    Task<int> CountLiveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LiveAccessKey>> ListLiveAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid userId, Guid accessKeyId, CancellationToken cancellationToken = default);

    Task<AccessKeyGrant?> FindAsync(string key, CancellationToken cancellationToken = default);

    Task RecordUseAsync(Guid accessKeyId, DateTimeOffset usedAt, CancellationToken cancellationToken = default);
}

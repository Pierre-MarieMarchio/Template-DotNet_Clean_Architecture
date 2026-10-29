using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Access.Tables;
using AppTemplate.Infrastructure.Core.Common.Saving;

namespace AppTemplate.Infrastructure.Auth.Features.AccessKeys.Adapters.Services;

internal sealed class AccessKeysService(
    IAccessKeyTable table,
    IContextUnitOfWork<AuthDbContext> unitOfWork,
    IDateTimeProvider dateTimeProvider) : IAccessKeysService
{
    internal static string ComputeHash(string key) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task<IssuedAccessKey> IssueAsync(
        Guid userId,
        string name,
        IReadOnlyList<string> scopes,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;
        string key = AccessKeyPolicy.KeyPrefix
            + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(AccessKeyPolicy.SecretSizeInBytes));
        string prefix = key[..AccessKeyPolicy.DisplayPrefixLength];

        Guid id = table.Add(userId, name, ComputeHash(key), prefix, scopes, now, expiresAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new IssuedAccessKey(id, key, prefix, now);
    }

    public async Task<int> CountLiveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await table.CountLiveAsync(userId, dateTimeProvider.UtcNow, cancellationToken);

    public async Task<IReadOnlyList<LiveAccessKey>> ListLiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await table.ListLiveAsync(userId, dateTimeProvider.UtcNow, cancellationToken);

    public async Task<bool> RevokeAsync(
        Guid userId,
        Guid accessKeyId,
        CancellationToken cancellationToken = default) =>
        await table.RevokeAsync(userId, accessKeyId, dateTimeProvider.UtcNow, cancellationToken);

    public async Task<AccessKeyGrant?> FindAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return await table.FindAsync(ComputeHash(key), cancellationToken);
    }

    public async Task RecordUseAsync(
        Guid accessKeyId,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken = default) =>
        await table.MarkUsedAsync(
            accessKeyId,
            usedAt,
            usedAt - AccessKeyPolicy.LastUseResolution,
            cancellationToken);
}

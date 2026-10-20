using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;

internal sealed class S3FileContentInventory(IAmazonS3 client, IOptions<FilesStorageS3Options> options)
    : IFileContentInventory
{
    public async Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        using var budget = BucketBudget.Start(cancellationToken);

        var response = await client.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = options.Value.BucketName,
                Prefix = prefix,
                MaxKeys = pageSize,
                StartAfter = startAfter,
            },
            budget.Token);

        List<string> keys = response.S3Objects is null ? [] : [.. response.S3Objects.Select(stored => stored.Key)];

        string? nextCursor = response.IsTruncated is true && keys.Count > 0 ? keys[^1] : null;

        return PagedResult.Keyset(keys, pageSize, nextCursor);
    }
}

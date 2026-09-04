using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Inventories;

/// <summary>
/// <see cref="IFileContentInventory"/> over an S3-compatible object store, on the store's own
/// listing call.
/// <para>
/// <b>The continuation token is the store's, passed back untouched.</b> It encodes where the walk
/// stopped in a form only the store parses, and the sweep that reads this depends on the listing
/// being ordered and resumable — so anything this adapter did to the token would be a second
/// implementation of paging, disagreeing with the first at exactly the moment a page boundary falls
/// between two keys.
/// </para>
/// <para>
/// A listing carries no total, and the page below therefore has none: no object store will count its
/// contents to answer one, which is why <see cref="PagedResult{TItem}"/>'s keyset half is the shape
/// the port asks for.
/// </para>
/// </summary>
internal sealed class S3FileContentInventory(IAmazonS3 client, IOptions<StorageOptions> options)
    : IFileContentInventory
{
    public async Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? continuationToken,
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
                ContinuationToken = continuationToken,
            },
            budget.Token);

        // The token is only meaningful on a truncated page. S3 leaves it null when the walk is
        // finished, but a compatible store echoing the previous one would put the caller in a loop
        // that never ends and re-deletes the same page for ever.
        string? nextCursor = response.IsTruncated is true ? response.NextContinuationToken : null;

        return PagedResult.Keyset(KeysOf(response), pageSize, nextCursor);
    }

    private static List<string> KeysOf(ListObjectsV2Response response) =>
        response.S3Objects is null ? [] : [.. response.S3Objects.Select(stored => stored.Key)];
}

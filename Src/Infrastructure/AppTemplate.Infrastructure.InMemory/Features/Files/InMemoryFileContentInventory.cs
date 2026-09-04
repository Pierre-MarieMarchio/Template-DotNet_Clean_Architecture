using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentInventory"/> over <see cref="StoredObjects"/>.
/// <para>
/// <b>It pages the way an object store pages, and that is the point of it existing at all.</b> The
/// orphan sweep walks this port page by page and deletes what no row names, so a double that
/// answered every listing in one page would leave the sweep's paging — the part that can loop for
/// ever or skip a page — exercised by nothing. The keys are ordered and the token is the last key
/// served, which is the same keyset arrangement a real listing has.
/// </para>
/// </summary>
internal sealed class InMemoryFileContentInventory(StoredObjects objects) : IFileContentInventory
{
    public Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        cancellationToken.ThrowIfCancellationRequested();

        var remaining = objects.KeysUnder(prefix)
            .Where(key => continuationToken is null || string.CompareOrdinal(key, continuationToken) > 0)
            .ToList();

        List<string> page = [.. remaining.Take(pageSize)];

        // A token only when there is something after this page. Handing one back on the last page is
        // how a sweep loops for ever over an empty tail.
        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }
}

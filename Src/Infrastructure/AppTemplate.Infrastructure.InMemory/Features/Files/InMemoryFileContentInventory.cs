using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentInventory"/> over <see cref="StoredObjects"/> that pages as an object store
/// does (ordinal order, resuming after the last key served), so the orphan sweep's paging is exercised.
/// </summary>
internal sealed class InMemoryFileContentInventory(StoredObjects objects) : IFileContentInventory
{
    public Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        cancellationToken.ThrowIfCancellationRequested();

        var remaining = objects.KeysUnder(prefix)
            .Where(key => startAfter is null || string.CompareOrdinal(key, startAfter) > 0)
            .ToList();

        List<string> page = [.. remaining.Take(pageSize)];

        // A token only when something follows this page; one on the last page loops a sweep for ever.
        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }
}

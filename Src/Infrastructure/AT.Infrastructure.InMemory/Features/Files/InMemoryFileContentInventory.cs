using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

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

        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }
}

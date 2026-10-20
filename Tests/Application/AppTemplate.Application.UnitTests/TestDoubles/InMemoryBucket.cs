using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;

namespace AppTemplate.Application.UnitTests.TestDoubles;

internal sealed class InMemoryBucket : IFileContentInventory, IFileContentStore
{
    private readonly SortedSet<string> _keys = new(StringComparer.Ordinal);

    public List<string?> ListedAfter { get; } = [];

    public IReadOnlyCollection<string> Keys => _keys;

    public void Put(params string[] keys) => _keys.UnionWith(keys);

    public Task<PagedResult<string>> ListKeysAsync(
        string prefix,
        string? startAfter,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ListedAfter.Add(startAfter);

        var remaining = _keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Where(key => startAfter is null || string.CompareOrdinal(key, startAfter) > 0)
            .ToList();

        List<string> page = [.. remaining.Take(pageSize)];
        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        _keys.Remove(objectKey);

        return Task.CompletedTask;
    }

    public Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The orphan sweep never writes.");

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The orphan sweep never reads content.");

    public Task<StoredObjectDescription?> DescribeAsync(string objectKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The orphan sweep never describes content.");
}

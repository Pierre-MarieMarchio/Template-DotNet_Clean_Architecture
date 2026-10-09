using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Inventories;

/// <summary>
/// <see cref="IFileContentInventory"/> over the directory <c>FileSystemFileContentStore</c> writes
/// to. Keys are the files' paths relative to the root, with <c>/</c> separators, in ordinal order —
/// the order an S3 listing has. Temporary files of writes in flight are not objects and are skipped.
/// </summary>
internal sealed class FileSystemFileContentInventory(IOptions<FilesStorageFileSystemOptions> options)
    : IFileContentInventory
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

        string root = Path.GetFullPath(options.Value.RootPath);
        string start = StartDirectory(root, prefix);

        // Enumerating the whole subtree per page is the price of having no index; fine at the scale
        // a local disk serves, and what keeps the walk resumable from a key alone.
        List<string> remaining = Directory.Exists(start)
            ? [.. Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).StartsWith(FileSystemFileContentStore.TemporaryFilePrefix, StringComparison.Ordinal))
                .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .Where(key => startAfter is null || string.CompareOrdinal(key, startAfter) > 0)
                .Order(StringComparer.Ordinal)]
            : [];

        List<string> page = [.. remaining.Take(pageSize)];
        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }

    /// <summary>The deepest directory the prefix fully names, so the walk skips the rest of the root.</summary>
    private static string StartDirectory(string root, string prefix)
    {
        int lastSeparator = prefix.LastIndexOf('/');

        return lastSeparator <= 0
            ? root
            : FileSystemFileContentStore.PathFor(root, prefix[..lastSeparator]);
    }
}

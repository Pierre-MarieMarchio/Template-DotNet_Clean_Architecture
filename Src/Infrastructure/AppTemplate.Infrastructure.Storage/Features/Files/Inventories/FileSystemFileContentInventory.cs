using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using AppTemplate.Infrastructure.Storage.Features.Files.Stores;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Inventories;

/// <summary>
/// <see cref="IFileContentInventory"/> over the directory <c>FileSystemFileContentStore</c> writes
/// to. Keys are the files' paths relative to the root, with <c>/</c> separators, in ordinal order —
/// the order an S3 listing has.
/// <para>
/// Temporary files of writes are not objects and are never listed. One older than
/// <see cref="StaleTemporaryFileAge"/> is left by a write that crashed, and no key will ever name it,
/// so the listing deletes it as it walks past: the orphan sweep reclaims it on its next pass.
/// </para>
/// </summary>
internal sealed class FileSystemFileContentInventory(IOptions<FilesStorageFileSystemOptions> options)
    : IFileContentInventory
{
    /// <summary>Far beyond any write, which refreshes the file's modification time as bytes arrive.</summary>
    internal static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromDays(1);

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
        List<string> remaining = [];

        // Enumerating the whole subtree per page is the price of having no index; fine at the scale
        // a local disk serves, and what keeps the walk resumable from a key alone.
        if (Directory.Exists(start))
        {
            foreach (string path in Directory.EnumerateFiles(start, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(path).StartsWith(FileSystemFileContentStore.TemporaryFilePrefix, StringComparison.Ordinal))
                {
                    DeleteIfStale(path);

                    continue;
                }

                string key = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

                if (key.StartsWith(prefix, StringComparison.Ordinal)
                    && (startAfter is null || string.CompareOrdinal(key, startAfter) > 0))
                {
                    remaining.Add(key);
                }
            }
        }

        remaining.Sort(StringComparer.Ordinal);

        List<string> page = [.. remaining.Take(pageSize)];
        string? nextCursor = remaining.Count > page.Count ? page[^1] : null;

        return Task.FromResult(PagedResult.Keyset(page, pageSize, nextCursor));
    }

    /// <summary>
    /// Compared with the disk's own clock, which stamped the file, rather than the application's,
    /// which a host may hold fixed.
    /// </summary>
    private static void DeleteIfStale(string path)
    {
        try
        {
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > StaleTemporaryFileAge)
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still open, or already gone: the next listing tries again.
        }
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

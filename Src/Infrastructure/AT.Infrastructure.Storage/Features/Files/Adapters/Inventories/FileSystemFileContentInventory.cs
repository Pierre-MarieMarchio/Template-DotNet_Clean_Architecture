using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;

internal sealed class FileSystemFileContentInventory(IOptions<FilesStorageFileSystemOptions> options)
    : IFileContentInventory
{
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
        }
    }

    private static string StartDirectory(string root, string prefix)
    {
        int lastSeparator = prefix.LastIndexOf('/');

        return lastSeparator <= 0
            ? root
            : FileSystemFileContentStore.PathFor(root, prefix[..lastSeparator]);
    }
}

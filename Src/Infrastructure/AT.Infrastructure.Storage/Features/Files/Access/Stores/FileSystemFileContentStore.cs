using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;

internal sealed class FileSystemFileContentStore(IOptions<FilesStorageFileSystemOptions> options) : IFileContentStore
{
    internal const string TemporaryFilePrefix = ".";

    private const int _bufferSize = 81920;

    public async Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        string target = PathFor(objectKey);
        string directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        string temporary = Path.Combine(
            directory,
            $"{TemporaryFilePrefix}{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");

        try
        {
            if (!await CopyMatchingAsync(content, temporary, sizeInBytes, sha256Hex, cancellationToken))
            {
                return ContentWriteOutcome.ContentMismatch;
            }

            try
            {
                File.Move(temporary, target, overwrite: false);
            }
            catch (IOException) when (File.Exists(target))
            {
            }

            return ContentWriteOutcome.Written;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<Stream?>(TryOpen(PathFor(objectKey)));
    }

    public async Task<StoredObjectDescription?> DescribeAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        await using var stream = TryOpen(PathFor(objectKey));

        if (stream is null)
        {
            return null;
        }

        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken);

        return new StoredObjectDescription(stream.Length, Convert.ToHexStringLower(digest));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            File.Delete(PathFor(objectKey));
        }
        catch (DirectoryNotFoundException)
        {
        }

        return Task.CompletedTask;
    }

    internal static string PathFor(string rootPath, string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, objectKey));

        if (!path.StartsWith(root, StringComparison.Ordinal) || path.Length == root.Length)
        {
            throw new ArgumentException($"The object key '{objectKey}' does not name a file under the storage root.", nameof(objectKey));
        }

        return path;
    }

    private string PathFor(string objectKey) => PathFor(options.Value.RootPath, objectKey);

    private static async Task<bool> CopyMatchingAsync(
        Stream content,
        string temporary,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[_bufferSize];
        long written = 0;

        await using (var file = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            _bufferSize,
            FileOptions.Asynchronous))
        {
            int read;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                written += read;

                if (written > sizeInBytes)
                {
                    return false;
                }

                hash.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await file.FlushAsync(cancellationToken);
        }

        return written == sizeInBytes
            && string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                _bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}

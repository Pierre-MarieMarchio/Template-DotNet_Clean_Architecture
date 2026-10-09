using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentStore"/> over <see cref="StoredObjects"/>, simulating no failure; a test
/// needing a failing store substitutes one.
/// </summary>
internal sealed class InMemoryFileContentStore(StoredObjects objects) : IFileContentStore
{
    public async Task<ContentWriteOutcome> WriteAsync(
        string objectKey,
        Stream content,
        string declaredMediaType,
        long sizeInBytes,
        string sha256Hex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaredMediaType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeInBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        byte[] bytes = buffer.ToArray();

        if (bytes.LongLength != sizeInBytes
            || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            return ContentWriteOutcome.ContentMismatch;
        }

        objects.Deposit(objectKey, declaredMediaType, bytes);

        return ContentWriteOutcome.Written;
    }

    public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = objects.Find(objectKey);

        return Task.FromResult<Stream?>(stored is null
            ? null
            : new MemoryStream(stored.Content.ToArray(), writable: false));
    }

    public Task<StoredObjectDescription?> DescribeAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = objects.Find(objectKey);

        return Task.FromResult(stored is null
            ? null
            : new StoredObjectDescription(stored.SizeInBytes, stored.Checksum));
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        cancellationToken.ThrowIfCancellationRequested();

        objects.Remove(objectKey);

        return Task.CompletedTask;
    }
}

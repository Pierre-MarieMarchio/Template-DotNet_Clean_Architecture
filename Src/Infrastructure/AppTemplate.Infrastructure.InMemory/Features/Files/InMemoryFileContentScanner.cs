using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

/// <summary>
/// An <see cref="IFileContentScanner"/> that answers what <see cref="ArrangedScans"/> holds for the
/// key, and opens no socket. It reads the content to the end, as a real scanner does.
/// </summary>
internal sealed class InMemoryFileContentScanner(ArrangedScans scans) : IFileContentScanner
{
    public async Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        long sizeInBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);

        await content.CopyToAsync(Stream.Null, cancellationToken);

        return scans.VerdictFor(objectKey);
    }
}

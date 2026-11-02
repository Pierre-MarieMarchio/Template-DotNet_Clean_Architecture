using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.InMemory.Features.Files;

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

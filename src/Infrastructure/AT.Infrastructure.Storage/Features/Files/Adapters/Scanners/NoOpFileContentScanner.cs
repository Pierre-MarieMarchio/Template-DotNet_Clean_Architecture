using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;

internal sealed class NoOpFileContentScanner : IFileContentScanner
{
    public Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        long sizeInBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(FileScanOutcome.Clean);
    }
}

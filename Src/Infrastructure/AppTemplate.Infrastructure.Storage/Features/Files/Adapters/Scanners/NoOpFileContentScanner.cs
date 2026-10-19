using AppTemplate.Application.Features.Files.Ports.FileContentScanner;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;

/// <summary>
/// <see cref="IFileContentScanner"/> when <c>Files:Scanning:Enabled</c> is false: nothing looks, so
/// nothing is found. The type check on the file's leading bytes still runs; <c>SECURITY.md</c>
/// records the missing antivirus verdict.
/// </summary>
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

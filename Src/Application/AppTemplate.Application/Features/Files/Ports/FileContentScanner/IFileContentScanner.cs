namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

public interface IFileContentScanner
{
    Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        long sizeInBytes,
        CancellationToken cancellationToken = default);
}

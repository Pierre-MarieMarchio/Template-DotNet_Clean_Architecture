namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

/// <summary>
/// A malware scanner. It reports what it found and decides nothing; what a verdict means is
/// <c>StoredFileContentPolicy</c>'s. It unpacks nothing in this process.
/// </summary>
public interface IFileContentScanner
{
    /// <summary>
    /// Scans <paramref name="content"/>, reading it forwards once without disposing it. Never throws to report a
    /// verdict or an outage: an unreachable scanner is <see cref="FileScanStatus.Unavailable"/>.
    /// </summary>
    /// <param name="objectKey">For log lines only.</param>
    /// <param name="sizeInBytes">The confirmed length, so a scanner with a ceiling answers
    /// <see cref="FileScanStatus.NotInspectable"/> before streaming a byte.</param>
    Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        long sizeInBytes,
        CancellationToken cancellationToken = default);
}

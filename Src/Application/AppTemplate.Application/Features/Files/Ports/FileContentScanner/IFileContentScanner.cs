namespace AppTemplate.Application.Features.Files.Ports.FileContentScanner;

/// <summary>
/// A malware scanner. It reports what it found and decides nothing: what a verdict means for the
/// file is <c>StoredFileContentPolicy</c>'s.
/// <para>
/// An implementation unpacks nothing in this process; a scanner that unpacks archives does so in its
/// own address space, under its own limits.
/// </para>
/// </summary>
public interface IFileContentScanner
{
    /// <summary>
    /// Scans <paramref name="content"/>, the bytes stored under <paramref name="objectKey"/>. Reads
    /// the stream forwards, once, and does not dispose it.
    /// <para>
    /// Never throws to report a verdict or an outage: an unreachable scanner is
    /// <see cref="FileScanStatus.Unavailable"/>, which the caller retries.
    /// </para>
    /// </summary>
    /// <param name="objectKey">For log lines only; the bytes come from <paramref name="content"/>.</param>
    Task<FileScanOutcome> ScanAsync(string objectKey, Stream content, CancellationToken cancellationToken = default);
}

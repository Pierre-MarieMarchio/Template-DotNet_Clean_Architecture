using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Features.Files.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Scanners;

/// <summary>
/// <see cref="IFileContentScanner"/> over a <c>clamd</c> daemon. <c>clamd</c> does not speak HTTP, so
/// the hosts' outbound policy does not reach it; <see cref="ScannerBudget"/> restates that budget.
/// </summary>
internal sealed class ClamAvFileContentScanner(
    IOptions<FilesScanningOptions> options,
    ILogger<ClamAvFileContentScanner> logger) : IFileContentScanner
{
    public async Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);

        var scanning = options.Value;

        // Decided before a byte is sent when the length is known; otherwise while sending.
        if (content.CanSeek && content.Length - content.Position > scanning.MaxScannableBytes)
        {
            return FileScanOutcome.NotInspectable;
        }

        using var budget = ScannerBudget.Start(cancellationToken);

        try
        {
            return await ClamAvScanner.ScanAsync(
                scanning.Host,
                scanning.Port,
                content,
                scanning.MaxScannableBytes,
                budget.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Scanning '{ObjectKey}' ran past its {Budget} budget; the next pass will try again.",
                objectKey,
                ScannerBudget.TotalTimeout);

            return FileScanOutcome.Unavailable;
        }
    }
}

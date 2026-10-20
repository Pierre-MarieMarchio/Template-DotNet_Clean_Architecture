using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Features.Files.Configuration.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Scanners;

internal sealed class ClamAvFileContentScanner(
    IOptions<FilesScanningOptions> options,
    ILogger<ClamAvFileContentScanner> logger) : IFileContentScanner
{
    public async Task<FileScanOutcome> ScanAsync(
        string objectKey,
        Stream content,
        long sizeInBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(content);

        var scanning = options.Value;

        if (sizeInBytes > scanning.MaxScannableBytes)
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
                "Scanning '{ObjectKey}' ran past its {Budget} budget; the file's inspection is deferred.",
                objectKey,
                ScannerBudget.TotalTimeout);

            return FileScanOutcome.Unavailable;
        }
    }
}

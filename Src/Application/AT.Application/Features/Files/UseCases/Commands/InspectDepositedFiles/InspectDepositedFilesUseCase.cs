using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Configuration.Options;
using AppTemplate.Application.Features.Files.Model.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

public sealed class InspectDepositedFilesUseCase(
    IStoredFileRepository repository,
    IFileContentStore content,
    IFileContentScanner scanner,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    IOptions<FilesOptions> options,
    ILogger<InspectDepositedFilesUseCase> logger) : IInspectDepositedFilesUseCase
{
    internal const int BatchSize = 20;

    private const string _leaseName = "apptemplate.files.inspect-deposited";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int decided = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async cancellationToken => decided = await InspectBatchAsync(cancellationToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Inspection pass skipped: another host holds the '{LeaseName}' lease.",
                _leaseName);
        }

        return decided;
    }

    private async Task<int> InspectBatchAsync(CancellationToken cancellationToken)
    {
        var candidates = await repository.GetDepositedAsync(
            dateTimeProvider.UtcNow,
            BatchSize,
            cancellationToken);

        int decided = 0;
        int deferred = 0;

        foreach (var storedFile in candidates)
        {
            (var verdict, var scan) = await InspectAsync(storedFile, cancellationToken);
            var now = dateTimeProvider.UtcNow;

            if (verdict == ContentDecision.Retry)
            {
                storedFile.DeferInspection(now);
            }
            else
            {
                Apply(verdict, storedFile, scan, now);
            }

            if (!await TryCommitAsync(storedFile, cancellationToken))
            {
                break;
            }

            if (verdict == ContentDecision.Retry)
            {
                deferred++;
            }
            else
            {
                decided++;
            }
        }

        ReportDeferrals(deferred);

        return decided;
    }

    private async Task<bool> TryCommitAsync(StoredFile storedFile, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (ConcurrencyConflictException exception)
        {
            logger.LogWarning(
                exception,
                "Stored file {StoredFileId} was changed by somebody else while it was being inspected; " +
                "this pass ends here and the next one reads it afresh.",
                storedFile.Id);

            return false;
        }
    }

    private async Task<(ContentDecision Verdict, FileScanOutcome? Scan)> InspectAsync(
        StoredFile storedFile,
        CancellationToken cancellationToken)
    {
        string objectKey = storedFile.ObjectKey.Value;
        var category = options.Value.Find(storedFile.Category);
        bool scans = category?.Scan ?? true;
        bool verifiesSignature = category?.VerifySignature ?? true;

        try
        {
            await using var stream = await content.OpenReadAsync(objectKey, cancellationToken);

            if (stream is null)
            {
                logger.LogWarning(
                    "Nothing is stored under '{ObjectKey}', although a file's deposit was confirmed " +
                    "against it. It stays unavailable and is deferred.",
                    objectKey);

                return (ContentDecision.Retry, FileScanOutcome.Unavailable);
            }

            byte[] head = new byte[StoredFileContentPolicy.MaxHeadBytes];

            int read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);

            FileScanOutcome? scan = null;

            if (scans)
            {
                await using var whole = new ReplayedHeadStream(head.AsMemory(0, read), stream);
                scan = await scanner.ScanAsync(objectKey, whole, storedFile.Size.Bytes, cancellationToken);
            }

            var verdict = StoredFileContentPolicy.Decide(
                storedFile.DeclaredMediaType,
                scan,
                head.AsSpan(0, read),
                verifiesSignature);

            return (verdict, scan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "The content under '{ObjectKey}' could not be read for inspection; it is deferred.",
                objectKey);

            return (ContentDecision.Retry, FileScanOutcome.Unavailable);
        }
    }

    private void Apply(ContentDecision verdict, StoredFile storedFile, FileScanOutcome? scan, DateTimeOffset now)
    {
        if (verdict == ContentDecision.Release)
        {
            storedFile.MakeAvailable(now);

            return;
        }

        storedFile.Quarantine(now);

        logger.LogError(
            "Stored file {StoredFileId} was quarantined. Category {Category}, declared " +
            "{DeclaredMediaType}, scan {ScanStatus}, signature {MalwareSignature}.",
            storedFile.Id,
            storedFile.Category.Value,
            storedFile.DeclaredMediaType.Value,
            scan?.Status.ToString() ?? "skipped",
            scan?.MalwareSignature ?? "none");
    }

    private void ReportDeferrals(int deferred)
    {
        if (deferred > 0)
        {
            logger.LogWarning(
                "{Deferred} deposited files could not be inspected; each stays unavailable and is " +
                "offered again after its backoff.",
                deferred);
        }
    }
}

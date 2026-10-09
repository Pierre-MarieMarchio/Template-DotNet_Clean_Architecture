using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.Options;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentScanner;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>
/// Reads the content of files whose deposit is confirmed and either releases them or quarantines
/// them: the head decides what the file is, the scanner whether it carries malware, and
/// <see cref="StoredFileContentPolicy"/> what that means. The file's category says whether its
/// content is scanned and whether its head must match its declared type; a category no longer
/// configured gets both.
/// <para>
/// A periodic pass rather than a step of confirmation, because reading a whole file does not belong
/// inside an HTTP request. It re-derives its work from the rows, so a pass that never ran leaves
/// files waiting and a pass that ran twice finds nothing the second time. It runs from a background
/// job and must not read <see cref="ICurrentUser"/>.
/// </para>
/// </summary>
public sealed class InspectDepositedFilesUseCase(
    IStoredFileRepository repository,
    IFileContentStore content,
    IFileContentScanner scanner,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    IOptions<FilesOptions> options,
    ILogger<InspectDepositedFilesUseCase> logger) : IInspectDepositedFilesUseCase
{
    /// <summary>
    /// Files per pass. Small, because each one is read end to end.
    /// </summary>
    private const int _batchSize = 20;

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await repository.GetDepositedAsync(_batchSize, cancellationToken);

        var now = dateTimeProvider.UtcNow;
        int decided = 0;
        int deferred = 0;

        foreach (var storedFile in candidates)
        {
            (var verdict, var scan) = await InspectAsync(storedFile, cancellationToken);

            if (verdict == ContentDecision.Retry)
            {
                deferred++;
                continue;
            }

            Apply(verdict, storedFile, scan, now);
            decided++;
        }

        if (decided > 0)
        {
            // One commit for the batch: each transition is independent, so a failure loses a pass's
            // decisions and the next pass reaches the same verdicts.
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        ReportDeferrals(deferred);

        return decided;
    }

    /// <summary>
    /// Opens the content once: the head is read off the front, then the whole content — head
    /// included — is streamed to the scanner. A store that cannot be read is no verdict, never a
    /// refusal: quarantining over a store fault would destroy a file over somebody else's outage.
    /// </summary>
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
                    "against it. It stays unavailable; the next pass will try again.",
                    objectKey);

                return (ContentDecision.Retry, FileScanOutcome.Unavailable);
            }

            byte[] head = new byte[StoredFileContentPolicy.MaxHeadBytes];

            // ReadAtLeastAsync: a single read of a network stream returns whatever has arrived.
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
                "The content under '{ObjectKey}' could not be read for inspection; the next pass " +
                "will try again.",
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

        // The only record of why, and a log line rather than a response: the signature is a third
        // party's string that must not reach the uploader.
        logger.LogError(
            "Stored file {StoredFileId} was quarantined. Category {Category}, declared " +
            "{DeclaredMediaType}, scan {ScanStatus}, signature {MalwareSignature}.",
            storedFile.Id,
            storedFile.Category.Value,
            storedFile.DeclaredMediaType.Value,
            scan?.Status.ToString() ?? "skipped",
            scan?.MalwareSignature ?? "none");
    }

    /// <summary>
    /// Without it, a scanner unreachable for a day looks exactly like a system with nothing to inspect.
    /// </summary>
    private void ReportDeferrals(int deferred)
    {
        if (deferred > 0)
        {
            logger.LogWarning(
                "{Deferred} deposited files could not be inspected and stay unavailable until they " +
                "can be; the next pass will try again.",
                deferred);
        }
    }
}

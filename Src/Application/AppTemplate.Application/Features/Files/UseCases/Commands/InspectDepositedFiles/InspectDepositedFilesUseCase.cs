using AppTemplate.Application.Core.Common.Concurrency;
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
/// configured gets both. Runs from a background job and must not read <see cref="ICurrentUser"/>.
/// <para>
/// <b>Retry guarantee.</b> One host at a time, under the <c>apptemplate.files.inspect-deposited</c>
/// lease. Each file is committed on its own. A file that gets no verdict (bytes missing, store or
/// scanner unreachable) is deferred — one minute, doubled per attempt, capped at one hour — and goes
/// behind the files that have waited longer, so every deposited file is offered again at most an
/// hour after its last attempt and none can hold the front of the queue. An outage never releases or
/// refuses a file. A concurrency conflict ends the pass: the files committed before it keep their
/// verdicts, and the next pass reads the rest afresh.
/// </para>
/// </summary>
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
    /// <summary>Files per pass. Small, because each one is read end to end.</summary>
    internal const int BatchSize = 20;

    /// <summary>
    /// The lock key is derived from this name, so it is a contract across releases: renaming it lets
    /// two releases of a rolling deploy both run the pass.
    /// </summary>
    private const string _leaseName = "apptemplate.files.inspect-deposited";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int decided = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async leaseToken => decided = await InspectBatchAsync(leaseToken),
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

    /// <returns><c>false</c> when somebody else changed the file first; the failed write stays staged
    /// in this scope, so the pass has to stop.</returns>
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
                    "against it. It stays unavailable and is deferred.",
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
                "{Deferred} deposited files could not be inspected; each stays unavailable and is " +
                "offered again after its backoff.",
                deferred);
        }
    }
}

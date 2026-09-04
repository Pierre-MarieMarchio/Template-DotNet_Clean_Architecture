using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Policies;
using AppTemplate.Application.Features.Files.Ports.FileContentInspector;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;

/// <summary>
/// Looks at the content of files whose deposit is confirmed, and either releases them for serving or
/// refuses them. This is the step that turns the declared media type from a claim into a claim that
/// survived a check, and it is the only thing in this feature that ever reads a byte of a file.
/// <para>
/// <b>It is a pass rather than a step in the confirmation request, and the reason is the size of a
/// file.</b> Examining content means reading all of it: up to <c>FileSize.MaxBytes</c> out of the
/// object store and, where a scanner is configured, through it. Inside
/// <c>ConfirmFileUploadUseCase</c> that would put minutes of transfer and unbounded scanner time
/// inside an HTTP request whose body is a few hundred bytes of metadata — the same objection this
/// repository already sustained against resizing an image on the way out, and it does not become
/// weaker because the work is I/O instead of CPU. <c>CONTRIBUTING.md</c> settles the shape by name:
/// derivative work over a deposited file is "a sweep for available files without a derivative", with
/// an event only ever making it prompt.
/// </para>
/// <para>
/// <b>There is no event making it prompt, deliberately.</b> The fast path its siblings have would be
/// a consumer of a confirmation event — and consumers here are dispatched in-process after commit,
/// which is inside the very request the pass exists to keep this work out of. So the interval is the
/// whole of the latency, and it is set short for that reason rather than tuned against a cost.
/// </para>
/// <para>
/// <b>It re-derives its own precondition</b>, which is what the missing outbox requires of anything
/// in this repository: the question it asks is "which files have a confirmed deposit and no
/// verdict?", and the answer comes from the rows rather than from any message having arrived. A pass
/// that never ran leaves files waiting; a pass that ran twice finds nothing the second time, because
/// the first one moved every file it decided about out of the set.
/// </para>
/// <para>
/// <b>Runs from <c>AppTemplate.Worker</c>, so it must not read <see cref="ICurrentUser"/>:</b> that
/// host's implementation throws rather than invent an anonymous caller, and the files examined here
/// belong to every owner and to none of them.
/// </para>
/// <para>
/// <b>No leader lease</b>, on the same reasoning the two sweeps beside it give. Two hosts running
/// this at once inspect the same file twice and reach the same verdict — the bytes under a key never
/// change — and the second write loses on <c>xmin</c>, leaving one transition. The cost of the
/// duplication is a second read of the object; the cost of the lease would be that a single host
/// losing leadership stops every upload in the system from becoming readable.
/// </para>
/// </summary>
public sealed class InspectDepositedFilesUseCase(
    IStoredFileRepository repository,
    IFileContentInspector inspector,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILogger<InspectDepositedFilesUseCase> logger) : IInspectDepositedFilesUseCase
{
    /// <summary>
    /// How many files one pass will examine. Much smaller than the abandonment purge's batch,
    /// because a row there is deleted and a file here is read end to end: two hundred five-gigabyte
    /// objects in one pass would be a terabyte of transfer before anything committed.
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
            var inspection = await inspector.InspectAsync(storedFile.ObjectKey.Value, cancellationToken);
            var verdict = StoredFileContentPolicy.Decide(storedFile.DeclaredMediaType, inspection);

            if (verdict == ContentVerdict.Retry)
            {
                deferred++;
                continue;
            }

            Apply(verdict, storedFile, inspection, now);
            decided++;
        }

        if (decided == 0)
        {
            // Nothing staged, so there is nothing to commit and no round trip is worth paying for to
            // prove it — the same reasoning the abandonment purge gives.
            ReportDeferrals(deferred);

            return decided;
        }

        // One commit for the batch. Each file's transition is independent of every other's, so a
        // failure here loses a pass's decisions rather than corrupting any of them, and the next
        // pass finds exactly the same files still deposited and reaches the same verdicts.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        ReportDeferrals(deferred);

        return decided;
    }

    private void Apply(
        ContentVerdict verdict,
        StoredFile storedFile,
        ContentInspectionOutcome inspection,
        DateTimeOffset now)
    {
        if (verdict == ContentVerdict.Release)
        {
            storedFile.MakeAvailable(now);

            return;
        }

        storedFile.Quarantine(now);

        // The one place the detail of a refusal is recorded at all, and it is a log line rather than
        // a column or a response: it names a third party's signature, which is an operator's
        // business and must not reach the person who uploaded the file. Error level because a
        // refusal is a thing somebody should look at — it is either an attack, or a user whose file
        // will never work and who is not being told why.
        logger.LogError(
            "Stored file {StoredFileId} was quarantined. Declared {DeclaredMediaType}, inspection " +
            "{InspectionStatus}, signature {MalwareSignature}.",
            storedFile.Id,
            storedFile.DeclaredMediaType.Value,
            inspection.Status,
            inspection.MalwareSignature ?? "none");
    }

    /// <summary>
    /// Says out loud that files were left undecided. Without it a scanner that has been unreachable
    /// for a day looks exactly like a system with nothing to inspect: both report zero, and the only
    /// visible symptom is uploads that never become readable, reported by users rather than by the
    /// host.
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

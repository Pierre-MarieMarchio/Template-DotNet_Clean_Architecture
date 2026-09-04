using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

/// <summary>
/// Removes registrations that were never deposited against. A client that asked for an upload URL
/// and never used it leaves a pending row for ever otherwise, holding a slot against its owner's
/// quota that nothing would ever give back.
/// <para>
/// <b>Runs on a schedule, from <c>AppTemplate.Worker</c> — never from a request — which is why it
/// must not read <see cref="ICurrentUser"/>.</b> The worker registers <c>BackgroundCurrentUser</c>,
/// whose <c>UserId</c> throws rather than pretend an anonymous caller, so a use case that reached
/// for the caller here would fail on its first iteration. The files it removes belong to every
/// owner and to no caller, which is exactly why there is nobody to ask.
/// </para>
/// <para>
/// <b>No leader lease, deliberately.</b> <see cref="ILeaderLease"/>'s own documentation draws the
/// line: a pass that only issues idempotent deletes over a range already covered costs a second
/// replica a connection and nothing else — unlike the reminder pass, which sends mail. Two hosts
/// running this at once remove the same rows, one of them loses on <c>xmin</c>, and the result is
/// the same set of rows gone.
/// </para>
/// <para>
/// The query is the coarse filter and <see cref="StoredFile.IsAbandoned"/> is the decision, checked
/// again per file for the reason the repository's own documentation gives: the query is chosen for
/// an index, and only the aggregate can say whether one of the rows it returned may actually be
/// given up on.
/// </para>
/// <para>
/// Each file's <see cref="StoredFile.Delete"/> is announced alongside the removal, so any bytes a
/// client did deposit without confirming are reclaimed promptly. Nothing depends on that: an object
/// under a key no row names is reclaimed by <c>ReclaimOrphanedContentUseCase</c> whether the event
/// arrived or not.
/// </para>
/// </summary>
public sealed class PurgeAbandonedRegistrationsUseCase(
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider) : IPurgeAbandonedRegistrationsUseCase
{
    /// <summary>
    /// How long a registration may wait for its deposit. Longer than the upload window a grant is
    /// minted for, so a client using its full window is never swept out from under a deposit still
    /// in flight, and short enough that a quota slot comes back the same day.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

    /// <summary>Caps one pass to a bounded amount of work; a backlog beyond this is picked up by the
    /// next run rather than loaded all at once.</summary>
    private const int _batchSize = 200;

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = dateTimeProvider.UtcNow;

        var candidates = await repository.GetPendingRegisteredBeforeAsync(
            now - AbandonedAfter,
            _batchSize,
            cancellationToken);

        int purged = 0;

        foreach (var storedFile in candidates)
        {
            if (!storedFile.IsAbandoned(now, AbandonedAfter))
            {
                continue;
            }

            storedFile.Delete(now);
            repository.Remove(storedFile);
            purged++;
        }

        if (purged == 0)
        {
            // Nothing staged, so there is nothing to commit and no point paying for a round trip to
            // prove it.
            return purged;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return purged;
    }
}

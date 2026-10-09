using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

/// <summary>
/// Removes registrations that were never deposited against, so the quota slot each one holds comes
/// back. The query is the coarse filter and <see cref="StoredFile.IsAbandoned"/> the decision,
/// re-checked per file. Each removal announces <see cref="StoredFile.Delete"/> so deposited but
/// unconfirmed bytes are reclaimed promptly; <c>ReclaimOrphanedContentUseCase</c> reclaims them
/// regardless. Runs from a background job and must not read <see cref="ICurrentUser"/>.
/// <para>
/// <b>Retry guarantee.</b> One host at a time, under the <c>apptemplate.files.purge-abandoned</c>
/// lease. Idempotent: a batch is one commit, and a pass that fails — a row confirmed or deleted
/// meanwhile, an unreachable database — writes nothing and leaves rows the next pass selects again.
/// A row that stopped being pending is no longer selected, so it cannot block the others.
/// </para>
/// </summary>
public sealed class PurgeAbandonedRegistrationsUseCase(
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    ILogger<PurgeAbandonedRegistrationsUseCase> logger) : IPurgeAbandonedRegistrationsUseCase
{
    /// <summary>
    /// The lock key is derived from this name, so it is a contract across releases: renaming it lets
    /// two releases of a rolling deploy both run the pass.
    /// </summary>
    private const string _leaseName = "apptemplate.files.purge-abandoned";

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
        int purged = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async leaseToken => purged = await PurgeBatchAsync(leaseToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Abandoned-registration purge skipped: another host holds the '{LeaseName}' lease.",
                _leaseName);
        }

        return purged;
    }

    private async Task<int> PurgeBatchAsync(CancellationToken cancellationToken)
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
            return purged;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return purged;
    }
}

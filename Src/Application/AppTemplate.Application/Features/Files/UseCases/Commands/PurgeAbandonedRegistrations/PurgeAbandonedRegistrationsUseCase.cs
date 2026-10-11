using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

/// <summary>Removes registrations never deposited against, so the quota slot each holds comes back.
/// The query is the coarse filter, <see cref="StoredFile.IsAbandoned"/> the per-file decision; each
/// removal announces <see cref="StoredFile.Delete"/> so unconfirmed bytes are reclaimed promptly.
/// Background job: must not read <see cref="ICurrentUser"/>. Idempotent: a batch is one commit, so a
/// failed pass writes nothing and the next selects the same rows again.</summary>
public sealed class PurgeAbandonedRegistrationsUseCase(
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    ILogger<PurgeAbandonedRegistrationsUseCase> logger) : IPurgeAbandonedRegistrationsUseCase
{
    /// <summary>The lock key derives from this name: a cross-release contract, renaming it lets two releases both run the pass.</summary>
    private const string _leaseName = "apptemplate.files.purge-abandoned";

    /// <summary>
    /// How long a registration may wait for its deposit: longer than the upload window, so a deposit
    /// still in flight is never swept, short enough that a quota slot comes back the same day.
    /// </summary>
    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

    /// <summary>Bounds one pass; a larger backlog is picked up by the next run.</summary>
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

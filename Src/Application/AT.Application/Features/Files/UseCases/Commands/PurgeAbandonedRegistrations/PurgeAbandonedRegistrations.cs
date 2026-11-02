using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Repositories;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;

public sealed record PurgeAbandonedRegistrationsCommand : ICommand<Result<int>>;

public sealed class PurgeAbandonedRegistrationsCommandHandler(
    IStoredFileRepository repository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider dateTimeProvider,
    ILeaderLease lease,
    ILogger<PurgeAbandonedRegistrationsCommandHandler> logger) : ICommandHandler<PurgeAbandonedRegistrationsCommand, Result<int>>
{
    private const string _leaseName = "apptemplate.files.purge-abandoned";

    public static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(24);

    private const int _batchSize = 200;

    public async ValueTask<Result<int>> Handle(PurgeAbandonedRegistrationsCommand command, CancellationToken cancellationToken)
    {
        int purged = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async cancellationToken => purged = await PurgeBatchAsync(cancellationToken),
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

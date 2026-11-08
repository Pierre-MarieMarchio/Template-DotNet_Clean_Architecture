using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

public sealed record SweepDeletedAccountDataCommand : ICommand<Result<int>>;

public sealed class SweepDeletedAccountDataCommandHandler(
    IEnumerable<IDeletedAccountDataPolicy> policies,
    ILeaderLease lease,
    ILogger<SweepDeletedAccountDataCommandHandler> logger) : ICommandHandler<SweepDeletedAccountDataCommand, Result<int>>
{
    private const string _leaseName = "apptemplate.accounts.sweep-deleted-data";

    public async ValueTask<Result<int>> Handle(SweepDeletedAccountDataCommand command, CancellationToken cancellationToken)
    {
        int swept = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async cancellationToken => swept = await SweepAsync(cancellationToken),
            cancellationToken);

        if (!ranHere && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Deleted-account data sweep skipped: another host holds the '{LeaseName}' lease.",
                _leaseName);
        }

        return swept;
    }

    private async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        int swept = 0;

        foreach (var policy in policies)
        {
            var owners = await policy.ListDataOwnerIdsAsync(cancellationToken);

            foreach (var owner in owners)
            {
                try
                {
                    if (await policy.ApplyAsync(owner, cancellationToken))
                    {
                        swept++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Sweeping the data of deleted account {Owner} through {Policy} failed; "
                        + "the owner is skipped and retried at the next pass.",
                        owner.Value,
                        policy.GetType().Name);
                }
            }
        }

        return swept;
    }
}

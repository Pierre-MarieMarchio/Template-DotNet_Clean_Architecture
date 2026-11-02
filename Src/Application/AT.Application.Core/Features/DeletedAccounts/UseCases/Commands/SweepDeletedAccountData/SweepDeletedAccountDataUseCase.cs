using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

public sealed class SweepDeletedAccountDataUseCase(
    IEnumerable<IDeletedAccountDataPolicy> policies,
    ILeaderLease lease,
    ILogger<SweepDeletedAccountDataUseCase> logger) : ISweepDeletedAccountDataUseCase
{
    private const string _leaseName = "apptemplate.accounts.sweep-deleted-data";

    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
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

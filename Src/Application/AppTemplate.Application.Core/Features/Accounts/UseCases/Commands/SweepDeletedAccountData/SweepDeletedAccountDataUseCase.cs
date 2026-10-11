using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Application.Core.Features.Accounts.UseCases.Commands.SweepDeletedAccountData;

/// <summary>
/// The guarantee behind <see cref="AppTemplate.Application.Core.Common.Events.UserAccountDeleted"/>:
/// a fact published in process is not a fact delivered, and a consumer that threw — or was never
/// reached — leaves rows owned by an id that names no account. This pass finds them, by asking
/// each feature for the owner ids that still have data and letting the feature's own
/// <see cref="IDeletedAccountDataPolicy"/> re-derive the precondition against
/// <see cref="IUserAccountDirectory"/>. The whole pass runs under <see cref="ILeaderLease"/>: each
/// application is idempotent and per-owner committed, but systematically duplicating every pass is
/// a connection spent for nothing. Each owner is one transaction of the feature's own, committed
/// before the next begins, so a pass that dies mid-way has kept what it did and redoes nothing; an
/// owner whose application throws is logged and skipped — a poisoned owner must not become a
/// retention policy for everybody else's data.
/// </summary>
public sealed class SweepDeletedAccountDataUseCase(
    IEnumerable<IDeletedAccountDataPolicy> policies,
    ILeaderLease lease,
    ILogger<SweepDeletedAccountDataUseCase> logger) : ISweepDeletedAccountDataUseCase
{
    /// <summary>The lock key derives from this name: a cross-release contract, renaming it lets two releases both run the pass.</summary>
    private const string _leaseName = "apptemplate.accounts.sweep-deleted-data";

    /// <inheritdoc />
    public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        int swept = 0;

        bool ranHere = await lease.TryRunExclusivelyAsync(
            _leaseName,
            async leaseToken => swept = await SweepAsync(leaseToken),
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
                    // The policy re-derives the precondition: an account that exists again, or
                    // never left, leaves its data alone, whatever this pass assumed.
                    if (await policy.ApplyAsync(owner, cancellationToken))
                    {
                        swept++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Shutdown, not a failed owner: end the pass instead of logging it.
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

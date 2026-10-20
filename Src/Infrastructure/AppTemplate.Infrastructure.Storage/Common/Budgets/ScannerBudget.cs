namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

internal static class ScannerBudget
{
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalTimeout);

        return budget;
    }
}

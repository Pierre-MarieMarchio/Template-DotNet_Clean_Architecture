namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

internal static class BucketBudget
{
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    internal static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(30);

    internal const int MaxRetryAttempts = 3;

    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalRequestTimeout);

        return budget;
    }
}

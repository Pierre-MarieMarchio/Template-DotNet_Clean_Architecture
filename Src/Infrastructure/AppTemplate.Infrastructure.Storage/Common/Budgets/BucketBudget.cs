namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

/// <summary>
/// What one call to the object store may cost. The AWS SDK builds its own <c>HttpClient</c> and never
/// meets <c>IHttpClientFactory</c>, so the outbound HTTP policy cannot reach it and the budget is
/// restated here in the SDK's vocabulary with the policy's numbers (10 s per attempt, 30 s in total,
/// three retries). <b>If they move in <c>OutboundHttpExtensions</c>, they move here.</b>
/// </summary>
internal static class BucketBudget
{
    /// <summary>The SDK's per-request HTTP timeout.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The ceiling on one call including the SDK's retries. <c>AmazonS3Config.Timeout</c> bounds a
    /// single request only, so this is imposed from outside by cancelling.
    /// </summary>
    internal static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Three, as in the policy; the SDK's standard retry mode supplies backoff and jitter.</summary>
    internal const int MaxRetryAttempts = 3;

    /// <summary>Starts the total budget for one call; pass its token to the SDK so the deadline cancels it.</summary>
    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalRequestTimeout);

        return budget;
    }
}

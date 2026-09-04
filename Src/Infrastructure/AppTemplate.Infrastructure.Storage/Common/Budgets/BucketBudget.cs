namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

/// <summary>
/// What one call to the object store is allowed to cost.
/// <para>
/// <b>This module is the one place in the repository where the outbound HTTP policy does not
/// apply.</b> Each host installs that policy on <c>IHttpClientFactory</c>'s defaults — see
/// <c>Src/Presentation/AppTemplate.Api/Common/Outbound/OutboundHttpExtensions.cs</c> — so every
/// typed client any module registers inherits a timeout, a retry budget, a circuit breaker and a
/// concurrency bound without asking. The AWS SDK registers no typed client: it builds and caches its
/// own <c>HttpClient</c> inside <c>AmazonS3Config</c>, retries on its own schedule, and never meets
/// the factory. Nothing installed on the factory's defaults can reach it, and there is no supported
/// seam that would force it to. So the budget is restated here, in the SDK's own vocabulary, with
/// the same numbers — because a dependency reached on a budget nobody wrote down is a dependency
/// with no budget.
/// </para>
/// <para>
/// The numbers are the policy's, not new ones. Ten seconds per attempt: a store that has not
/// answered in ten seconds does not answer better at sixty, it just holds the caller longer. Thirty
/// seconds in total, which is what actually bounds a call — three retries of a ten-second attempt is
/// forty seconds of attempts, exactly as the resilience handler's own total timeout bounds its
/// retries. And thirty seconds sits inside <c>RequestTimeouts:Default</c>'s five minutes by a factor
/// of ten, so a request that touches the store several times still finishes inside its own deadline
/// and reports the store's failure rather than the caller's. <b>If either number moves in
/// <c>OutboundHttpExtensions</c>, it moves here.</b>
/// </para>
/// </summary>
internal static class BucketBudget
{
    /// <summary>What the SDK is given as its per-request HTTP timeout.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The ceiling on one call including every retry the SDK makes underneath it. The SDK has no
    /// setting for this — <c>AmazonS3Config.Timeout</c> bounds a single HTTP request, not the
    /// sequence — so it is imposed from outside, by cancelling the call.
    /// </summary>
    internal static readonly TimeSpan TotalRequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Three, as in the policy. The SDK's standard retry mode supplies the exponential backoff and
    /// the jitter, and it retries only what it classifies as transient — which for S3 is the same
    /// judgement the resilience handler makes about a response it did not write.
    /// </summary>
    internal const int MaxRetryAttempts = 3;

    /// <summary>
    /// Starts the total budget for one call. The caller disposes it, and passes its token to the SDK
    /// so that the deadline cancels the call rather than merely being observed after it.
    /// </summary>
    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalRequestTimeout);

        return budget;
    }
}

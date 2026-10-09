namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

/// <summary>
/// What one conversation with the malware scanner may cost. <c>clamd</c> speaks a line protocol over a
/// raw socket, so the outbound HTTP policy cannot reach it and the budget is restated here in socket
/// terms; the numbers are that policy's (10 s per attempt, 30 s in total) and move with it.
/// <para>
/// There is no retry: it would stream the whole object past the scanner again, and the periodic pass
/// offers the same file on its next tick anyway.
/// </para>
/// </summary>
internal static class ScannerBudget
{
    /// <summary>What a single socket operation is given.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The ceiling on the whole conversation, imposed by cancelling. It also bounds the scannable file
    /// size against <c>FilesScanningOptions.MaxScannableBytes</c>: raising the size without raising
    /// this makes a large scan a cancelled one, reported as no verdict and retried for ever.
    /// </summary>
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Starts the total budget for one conversation; pass its token to every socket operation.</summary>
    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalTimeout);

        return budget;
    }
}

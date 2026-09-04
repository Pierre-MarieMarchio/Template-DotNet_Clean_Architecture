namespace AppTemplate.Infrastructure.Storage.Common.Budgets;

/// <summary>
/// What one conversation with the malware scanner is allowed to cost.
/// <para>
/// <b>The hosts' outbound HTTP policy does not reach this, for the same reason it does not reach the
/// AWS SDK.</b> That policy is installed on <c>IHttpClientFactory</c>'s defaults, so it governs
/// typed clients and nothing else. <c>clamd</c> does not speak HTTP at all: it speaks a line
/// protocol over a raw TCP socket, so there is no <c>HttpClient</c> to configure, no handler to
/// insert and no seam the factory could reach. A dependency reached on a budget nobody wrote down is
/// a dependency with no budget, so the budget is restated here in the socket's own terms —
/// exactly as <see cref="BucketBudget"/> restates it in the SDK's.
/// </para>
/// <para>
/// The numbers are the policy's, not new ones: ten seconds per attempt, thirty seconds in total, and
/// thirty seconds sits inside <c>RequestTimeouts:Default</c>'s five minutes by a factor of ten. That
/// last relation matters less here than it does for the store — nothing on this path runs inside an
/// inbound request — but it is kept identical so that the three budgets in this repository are one
/// number rather than three. <b>If either moves in <c>OutboundHttpExtensions</c>, it moves here.</b>
/// </para>
/// <para>
/// <b>There is no retry, and that is a difference from the HTTP policy rather than an omission.</b>
/// Retrying means streaming the whole object past the scanner a second time; the caller is a
/// periodic pass that will offer the same file again on its next tick, at no cost to anyone waiting,
/// so a retry here would only turn one slow failure into three.
/// </para>
/// </summary>
internal static class ScannerBudget
{
    /// <summary>
    /// What a single socket operation — connecting, writing a chunk, reading the verdict — is given.
    /// A scanner that has not answered in ten seconds does not answer better at sixty.
    /// </summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The ceiling on the whole conversation, including the transfer of the object. It is what
    /// actually bounds the call, and it is imposed by cancelling rather than observed afterwards.
    /// <para>
    /// It is also the real limit on how large a file this template can scan, and it binds against
    /// <c>ContentInspectionOptions.MaxScannableBytes</c>: 25 MiB in thirty seconds is under a
    /// megabyte a second, which any daemon on the same network clears comfortably. Raising the size
    /// ceiling without raising this turns a large scan into a cancelled one, which is reported as no
    /// verdict and retried for ever.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Starts the total budget for one conversation. The caller disposes it, and passes its token to
    /// every socket operation so that the deadline cancels the call rather than merely being noticed
    /// after it.
    /// </summary>
    internal static CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TotalTimeout);

        return budget;
    }
}

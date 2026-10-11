namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>Runs work on one host at a time, so a loop that is only correct at a single replica can
/// be deployed at several. A pass that claims work in memory and commits the batch at the end needs
/// it; an idempotent delete over a range already covered does not.</summary>
/// <remarks>The work is a delegate, so the exclusion lasts exactly as long as the work.
/// <b>This is not a fencing token:</b> leadership can be lost mid-run, so the work must still be
/// safe if a second host begins it; a lease removes the systematic duplication of every pass, not
/// the overlap a failure can still produce.</remarks>
public interface ILeaderLease
{
    /// <summary>Takes the lease and runs <paramref name="work"/> under it, or returns straight away
    /// if another host holds it — never waits for the holder, since a standby that queued would run
    /// the work late instead of not at all.</summary>
    /// <param name="leaseName">Names what is being serialised, not who is asking; compared ordinally,
    /// so casing is significant.</param>
    /// <param name="work">Run at most once per call, with <paramref name="cancellationToken"/> passed through.</param>
    /// <returns><c>true</c> when the lease was taken and <paramref name="work"/> ran to completion;
    /// <c>false</c> when another host holds the lease and the work did not run at all.</returns>
    /// <remarks>An exception thrown by <paramref name="work"/> — <see cref="OperationCanceledException"/>
    /// included — reaches the caller with the lease already released, never reported as <c>false</c>:
    /// a caller that could not tell "it failed" from "somebody else has it" would log a failed pass
    /// as a quiet standby.</remarks>
    Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}

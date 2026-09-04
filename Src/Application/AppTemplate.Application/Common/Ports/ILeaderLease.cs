namespace AppTemplate.Application.Common.Ports;

/// <summary>
/// Runs work on one host at a time, so that a loop which is only correct at a single replica can be
/// deployed at several.
/// <para>
/// Not every scheduled job needs this. An idempotent delete over a range already covered costs a
/// second replica a connection and nothing else. A pass that claims work in memory and commits the
/// batch at the end does need it: two hosts both take the claim and both act, and only afterwards
/// does one of them lose on <c>xmin</c>. A claim of that kind defends against a host that died
/// mid-attempt, not against a concurrent pass.
/// </para>
/// </summary>
/// <remarks>
/// The work is a delegate rather than a handle the caller disposes, so that the exclusion lasts
/// exactly as long as the work: a handle handed back is released when the caller gets round to it,
/// and never if the caller forgets.
/// <para>
/// <b>This is not a fencing token.</b> Leadership can be lost mid-run — the mechanism behind it can
/// drop without the work being told — so the work still has to be safe if a second host begins it.
/// What a lease removes is the systematic duplication of every single pass, not the overlap that a
/// failure can still produce.
/// </para>
/// </remarks>
public interface ILeaderLease
{
    /// <summary>
    /// Takes the lease and runs <paramref name="work"/> under it, or returns straight away if
    /// another host holds it. Never waits for the holder: a standby that queued would run the work
    /// late instead of not at all, which for a scheduled pass is the same thing done twice.
    /// </summary>
    /// <param name="leaseName">Names what is being serialised, not who is asking. Two hosts naming
    /// the same lease must contend; the comparison is ordinal, so casing is significant.</param>
    /// <param name="work">Run at most once per call, with <paramref name="cancellationToken"/>
    /// passed through.</param>
    /// <returns><c>true</c> when the lease was taken and <paramref name="work"/> ran to completion;
    /// <c>false</c> when another host holds the lease and <paramref name="work"/> did not run at
    /// all.</returns>
    /// <remarks>
    /// An exception thrown by <paramref name="work"/> — <see cref="OperationCanceledException"/>
    /// included — reaches the caller with the lease already released. It is never reported as
    /// <c>false</c>: that answer means "somebody else has it", and a caller that could not tell the
    /// two apart would log a failed pass as a quiet standby.
    /// </remarks>
    Task<bool> TryRunExclusivelyAsync(
        string leaseName,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}

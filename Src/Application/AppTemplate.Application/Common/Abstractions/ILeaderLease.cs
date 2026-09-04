namespace AppTemplate.Application.Common.Abstractions;

/// <summary>
/// Runs work on one host at a time, so that a loop which is only correct at a single replica can be
/// deployed at several.
/// <para>
/// Not every scheduled job needs this. The two purges are idempotent deletes over a range that is
/// already covered, so a second replica wastes a connection and nothing else. The reminder pass is
/// the opposite: it claims each reminder in memory, notifies, and commits the whole batch at the
/// end, so two hosts ticking in the same second both take the claim and both send the mail — one of
/// them only loses on <c>xmin</c> afterwards, once the duplicate is already delivered. The claim
/// defends against a host that died mid-attempt, which is what it was written for, and not against
/// a concurrent pass.
/// </para>
/// </summary>
/// <remarks>
/// <b>Why the work is a delegate rather than a handle the caller disposes.</b> What this port sells
/// is that the exclusion lasts exactly as long as the work, and only an implementation owning both
/// ends can promise that: a handle handed back is released when the caller gets round to it, and
/// never if the caller forgets. Taking the work in also keeps this a single public interface — a
/// handle type would be a second one, produced by a factory and registered in no container, which
/// is a hole nothing in the build can see.
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

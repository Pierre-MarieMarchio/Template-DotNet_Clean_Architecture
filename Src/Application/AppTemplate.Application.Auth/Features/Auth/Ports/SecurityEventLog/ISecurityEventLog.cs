namespace AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;

/// <summary>
/// The audit trail for authentication itself: who signed in, who failed to, whose account locked,
/// and every point a credential was revoked or found already spent.
/// <para>
/// One capability: keeping the trail. Recording is one operation, not one per event — what varies
/// is the closed set of typed events below, each carrying only the data its own fact carries,
/// rather than the width of this interface. The other operation is the trail's own retention:
/// events older than the configured period are removed, so the trail is bounded by time rather
/// than growing for ever.
/// </para>
/// <para>
/// <b>Never given an email address.</b> Several call sites along this vertical — resending a
/// confirmation, checking a credential — answer identically whether or not the address exists,
/// specifically so a caller cannot enumerate accounts. A log line that named the address on one
/// branch and not another would leak exactly what the identical response is hiding. Every event
/// here therefore speaks in <see cref="Guid"/> user ids, never in the address a caller typed.
/// </para>
/// <para>
/// Fire-and-forget by design: an event here is a side effect of a decision the caller already
/// made, not a fact anything downstream waits on. A failure to record one must never fail the
/// request it describes.
/// </para>
/// </summary>
public interface ISecurityEventLog
{
    /// <summary>
    /// Records one fact and returns. It must not throw and must not fail the request that produced
    /// the fact — see above.
    /// </summary>
    void Record(SecurityEvent securityEvent);

    /// <summary>
    /// Removes every event older than the retention period, counted back from
    /// <paramref name="asOf"/>, and answers how many were removed. The period is the adapter's
    /// configuration, not the caller's: a scheduled purge has no business choosing how long evidence
    /// is kept.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

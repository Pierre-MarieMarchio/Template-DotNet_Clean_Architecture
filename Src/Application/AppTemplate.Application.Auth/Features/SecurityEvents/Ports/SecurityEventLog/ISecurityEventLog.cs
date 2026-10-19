namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

/// <summary>The audit trail for authentication itself: who signed in, who failed to, whose account
/// locked, and every point a credential was revoked or found already spent. Recording is one
/// operation — what varies is the closed set of typed events — and the other is the trail's
/// retention, bounded by time. <b>Never given an email address:</b> call sites answer identically
/// whether or not the address exists, so a log line naming it on one branch would leak what the
/// identical response hides — every event speaks in <see cref="Guid"/> user ids. Fire-and-forget by
/// design: a failure to record one must never fail the request it describes.</summary>
public interface ISecurityEventLog
{
    /// <summary>Records one fact and returns; it must not throw and must not fail the request that produced it.</summary>
    void Record(SecurityEvent securityEvent);

    /// <summary>
    /// Removes every event older than the retention period, counted back from
    /// <paramref name="asOf"/>, and answers how many. The period is the adapter's configuration:
    /// a scheduled purge has no business choosing how long evidence is kept.
    /// </summary>
    Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

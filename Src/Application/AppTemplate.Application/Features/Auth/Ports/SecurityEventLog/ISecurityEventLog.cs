namespace AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

/// <summary>
/// The audit trail for authentication itself: who signed in, who failed to, whose account locked,
/// and every point a credential was revoked or found already spent.
/// <para>
/// One operation, not one per event, so this stays one capability: recording an event. The typed
/// events below are the "jeu d'événements typés" — a closed set of facts, each with only the data
/// that fact carries — rather than a widening interface.
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
    void Record(SecurityEvent securityEvent);
}

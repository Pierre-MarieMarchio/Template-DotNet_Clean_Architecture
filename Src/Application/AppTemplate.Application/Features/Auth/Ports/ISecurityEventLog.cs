namespace AppTemplate.Application.Features.Auth.Ports;

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

/// <summary>Which authentication-relevant fact <see cref="SecurityEvent"/> carries.</summary>
public enum SecurityEventKind
{
    /// <summary>A credential check succeeded and the caller is about to be issued tokens.</summary>
    LoginSucceeded,

    /// <summary>A credential check failed.</summary>
    AuthenticationFailed,

    /// <summary>The failed-attempt threshold was crossed and the account is now locked out.</summary>
    AccountLockedOut,

    /// <summary>An account was created.</summary>
    Registered,

    /// <summary>A refresh token was revoked as part of an explicit sign-out.</summary>
    LoggedOut,

    /// <summary>Every live refresh-token grant for a user was revoked.</summary>
    RefreshTokenRevoked,

    /// <summary>
    /// A refresh token was presented after it had already been consumed — a replay of a stolen
    /// token, or the legitimate holder racing a copy they did not know existed.
    /// </summary>
    RefreshTokenReplayDetected,

    /// <summary>
    /// A user's security stamp was rotated, invalidating every credential issued against the old
    /// one. Nothing in this codebase rotates a stamp yet; the event exists so the feature that
    /// eventually does — a password change, an admin-forced sign-out — has an audit point to call
    /// from its first day rather than needing this port extended later.
    /// </summary>
    SecurityStampRotated,
}

/// <summary>
/// One fact for <see cref="ISecurityEventLog"/> to record. <see cref="UserId"/> is <c>null</c> only
/// for a failed authentication against an address that named no account at all — there is nothing
/// to identify that attempt by that is not the address itself. <see cref="Outcome"/> is set only for
/// <see cref="SecurityEventKind.AuthenticationFailed"/>.
/// </summary>
public sealed record SecurityEvent(SecurityEventKind Kind, Guid? UserId, CredentialCheckOutcome? Outcome = null)
{
    public static SecurityEvent LoginSucceeded(Guid userId) =>
        new(SecurityEventKind.LoginSucceeded, userId);

    public static SecurityEvent AuthenticationFailed(Guid? userId, CredentialCheckOutcome outcome) =>
        new(SecurityEventKind.AuthenticationFailed, userId, outcome);

    public static SecurityEvent AccountLockedOut(Guid userId) =>
        new(SecurityEventKind.AccountLockedOut, userId);

    public static SecurityEvent Registered(Guid userId) =>
        new(SecurityEventKind.Registered, userId);

    public static SecurityEvent LoggedOut(Guid userId) =>
        new(SecurityEventKind.LoggedOut, userId);

    public static SecurityEvent RefreshTokenRevoked(Guid userId) =>
        new(SecurityEventKind.RefreshTokenRevoked, userId);

    public static SecurityEvent RefreshTokenReplayDetected(Guid userId) =>
        new(SecurityEventKind.RefreshTokenReplayDetected, userId);

    public static SecurityEvent SecurityStampRotated(Guid userId) =>
        new(SecurityEventKind.SecurityStampRotated, userId);
}

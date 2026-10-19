namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

/// <summary>Which authentication-relevant fact <see cref="SecurityEvent"/> carries.</summary>
public enum SecurityEventKind
{
    /// <summary>A credential check succeeded and the caller is about to be issued tokens.</summary>
    LoginSucceeded,

    /// <summary>
    /// A credential check refused sign-in; which check refused travels on
    /// <see cref="SecurityEvent.Status"/>, where the response to the caller carries no such detail.
    /// </summary>
    AuthenticationFailed,

    /// <summary>A sign-in met the automatic lockout, the attempt that started it included.</summary>
    AccountLockedOut,

    /// <summary>An account was created.</summary>
    Registered,

    /// <summary>A refresh token was revoked as part of an explicit sign-out.</summary>
    LoggedOut,

    /// <summary>Every live refresh-token grant for a user was revoked.</summary>
    RefreshTokenRevoked,

    /// <summary>
    /// A refresh token was presented after it had already been consumed — a stolen token's replay,
    /// or the legitimate holder racing a copy they did not know existed.
    /// </summary>
    RefreshTokenReplayDetected,

    /// <summary>
    /// A user's security stamp was rotated, invalidating every credential issued against the old
    /// one; recorded by a password change and reset, both through
    /// <see cref="AppTemplate.Application.Auth.Features.Accounts.Model.Policies.CredentialInvalidationPolicy"/>.
    /// </summary>
    SecurityStampRotated,

    /// <summary>An administrator locked an account out, indefinitely, outside the automatic threshold above.</summary>
    AccountLockedByAdministrator,

    /// <summary>An administrative lockout was lifted.</summary>
    AccountUnlockedByAdministrator,

    /// <summary>A role was assigned; which one is on <see cref="SecurityEvent.Role"/>.</summary>
    RoleGranted,

    /// <summary>A role was taken away; which one is on <see cref="SecurityEvent.Role"/>.</summary>
    RoleRevoked,

    /// <summary>An account was removed from the store by an administrator.</summary>
    AccountDeleted,

    /// <summary>
    /// An account was removed from the store by its own holder, having proven its credentials
    /// again. The same ending as <see cref="AccountDeleted"/> reached by a different hand, and
    /// separate for it: one belongs in an audit of what administrators did, the other in an audit
    /// of what accounts did to themselves.
    /// </summary>
    AccountSelfDeleted,

    /// <summary>
    /// Two-factor sign-in was armed: a first code was verified and the account now requires one
    /// at every login.
    /// </summary>
    TwoFactorEnabled,

    /// <summary>Two-factor sign-in was turned off, after the current password was proven again.</summary>
    TwoFactorDisabled,

    /// <summary>
    /// A login's second step presented a live challenge with a code that did not match it — the
    /// six-digit equivalent of <see cref="AuthenticationFailed"/> for the first step.
    /// </summary>
    TwoFactorChallengeFailed,

    /// <summary>
    /// A login's second step was completed with a recovery code instead of the authenticator app —
    /// also the signal that one of the ten one-time codes is now gone.
    /// </summary>
    RecoveryCodeRedeemed,

    /// <summary>
    /// An administrator disabled two-factor sign-in on someone else's account, rather than the
    /// account proving its password through <see cref="TwoFactorDisabled"/>'s route.
    /// </summary>
    TwoFactorDisabledByAdministrator,

    /// <summary>
    /// A passkey was registered on the account, after the holder proved the session was their own.
    /// Its own fact beside <see cref="TwoFactorEnabled"/>: a passkey is a complete way in, not a
    /// second step on one.
    /// </summary>
    PasskeyRegistered,

    /// <summary>
    /// One of the account's passkeys was removed by its holder; with
    /// <see cref="PasskeyRegistered"/>, the two say which credentials were live over what period.
    /// </summary>
    PasskeyRemoved,

    /// <summary>
    /// A sign-in completed on a passkey alone; separate from <see cref="LoginSucceeded"/> so the
    /// trail says which credential a session started with.
    /// </summary>
    PasskeySignInSucceeded,

    /// <summary>
    /// A passkey sign-in was refused — a ceremony that did not verify, or an account no longer
    /// allowed to sign in. The user id is <c>null</c> exactly when the ceremony failed before an
    /// account was identified.
    /// </summary>
    PasskeySignInFailed,
}

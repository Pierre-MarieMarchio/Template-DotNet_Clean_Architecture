namespace AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

/// <summary>Which authentication-relevant fact <see cref="SecurityEvent"/> carries.</summary>
public enum SecurityEventKind
{
    /// <summary>A credential check succeeded and the caller is about to be issued tokens.</summary>
    LoginSucceeded,

    AuthenticationFailed,

    /// <summary>The failed-attempt threshold was crossed and the account is now locked out.</summary>
    AccountLockedOut,

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
    /// one. Recorded by a password change and by a password reset, both through
    /// <see cref="AppTemplate.Application.Features.Auth.Policies.CredentialInvalidationPolicy"/>.
    /// </summary>
    SecurityStampRotated,

    /// <summary>An administrator locked an account out, indefinitely, outside the automatic threshold above.</summary>
    AccountLockedByAdministrator,

    AccountUnlockedByAdministrator,

    RoleGranted,

    RoleRevoked,

    AccountDeleted,

    /// <summary>
    /// Two-factor sign-in was armed: a first code was verified and the account now requires one at
    /// every login.
    /// </summary>
    TwoFactorEnabled,

    /// <summary>Two-factor sign-in was turned off, after the current password was proven again.</summary>
    TwoFactorDisabled,

    /// <summary>
    /// A login's second step presented a challenge that was still live but a code that did not match
    /// it — the six-digit equivalent of <see cref="AuthenticationFailed"/> for the first step.
    /// </summary>
    TwoFactorChallengeFailed,

    /// <summary>
    /// A login's second step was completed with a recovery code instead of the authenticator app —
    /// worth its own fact, since it is also the signal that one of the ten one-time codes is now gone.
    /// </summary>
    RecoveryCodeRedeemed,

    /// <summary>
    /// An administrator disabled two-factor sign-in on someone else's account through
    /// <see cref="AppTemplate.Application.Features.Auth.Ports.TwoFactorAdministration.ITwoFactorAdministrationService"/>,
    /// rather than the account itself proving its password through <see cref="TwoFactorDisabled"/>'s route.
    /// </summary>
    TwoFactorDisabledByAdministrator,
}

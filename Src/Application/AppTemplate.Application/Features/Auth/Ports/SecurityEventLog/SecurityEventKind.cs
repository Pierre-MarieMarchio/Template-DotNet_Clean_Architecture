namespace AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

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
    /// one. Recorded by a password change and by a password reset, both through
    /// <see cref="AppTemplate.Application.Features.Auth.Policies.CredentialInvalidation"/>.
    /// </summary>
    SecurityStampRotated,
}

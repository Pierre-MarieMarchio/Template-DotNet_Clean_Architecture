using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;

/// <summary>
/// One fact for <see cref="ISecurityEventLog"/> to record. <see cref="UserId"/> is <c>null</c> only
/// for a failed authentication against an address that named no account at all — there is nothing
/// to identify that attempt by that is not the address itself. <see cref="Status"/> is set only for
/// <see cref="SecurityEventKind.AuthenticationFailed"/>. <see cref="Role"/> is set only for
/// <see cref="SecurityEventKind.RoleGranted"/> and <see cref="SecurityEventKind.RoleRevoked"/>.
/// </summary>
public sealed record SecurityEvent(
    SecurityEventKind Kind,
    Guid? UserId,
    CredentialCheckStatus? Status = null,
    string? Role = null)
{
    /// <summary>A credential check passed and tokens are about to be issued for it.</summary>
    public static SecurityEvent LoginSucceeded(Guid userId) =>
        new(SecurityEventKind.LoginSucceeded, userId);

    /// <summary>
    /// Sign-in was refused. <paramref name="userId"/> is <c>null</c> when the address named no
    /// account, since the address itself is the one thing this log will not carry.
    /// </summary>
    public static SecurityEvent AuthenticationFailed(Guid? userId, CredentialCheckStatus outcome) =>
        new(SecurityEventKind.AuthenticationFailed, userId, outcome);

    /// <summary>
    /// A sign-in met the automatic, timed lockout: on the attempt that crossed the failed-attempt
    /// threshold and on every attempt while it lasts.
    /// </summary>
    public static SecurityEvent AccountLockedOut(Guid userId) =>
        new(SecurityEventKind.AccountLockedOut, userId);

    /// <summary>An account was created.</summary>
    public static SecurityEvent Registered(Guid userId) =>
        new(SecurityEventKind.Registered, userId);

    /// <summary>A refresh token was revoked because its holder asked to sign out.</summary>
    public static SecurityEvent LoggedOut(Guid userId) =>
        new(SecurityEventKind.LoggedOut, userId);

    /// <summary>Every live grant the account held was revoked at once.</summary>
    public static SecurityEvent RefreshTokenRevoked(Guid userId) =>
        new(SecurityEventKind.RefreshTokenRevoked, userId);

    /// <summary>
    /// An already-spent refresh token was presented. The whole family is revoked with it, so this is
    /// also the moment the account was signed out everywhere.
    /// </summary>
    public static SecurityEvent RefreshTokenReplayDetected(Guid userId) =>
        new(SecurityEventKind.RefreshTokenReplayDetected, userId);

    /// <summary>The stamp behind every access token in circulation was replaced, failing them all.</summary>
    public static SecurityEvent SecurityStampRotated(Guid userId) =>
        new(SecurityEventKind.SecurityStampRotated, userId);

    /// <summary>An administrator suspended sign-in, with no timer to lift it.</summary>
    public static SecurityEvent AccountLockedByAdministrator(Guid userId) =>
        new(SecurityEventKind.AccountLockedByAdministrator, userId);

    /// <summary>An administrator lifted a suspension.</summary>
    public static SecurityEvent AccountUnlockedByAdministrator(Guid userId) =>
        new(SecurityEventKind.AccountUnlockedByAdministrator, userId);

    /// <summary>A role was granted, named here because which role it was is the point.</summary>
    public static SecurityEvent RoleGranted(Guid userId, string role) =>
        new(SecurityEventKind.RoleGranted, userId, Role: role);

    /// <summary>A role was revoked, named for the same reason <see cref="RoleGranted"/> names one.</summary>
    public static SecurityEvent RoleRevoked(Guid userId, string role) =>
        new(SecurityEventKind.RoleRevoked, userId, Role: role);

    /// <summary>An account was removed, by an administrator. Its id outlives it here, and in the rows that named it.</summary>
    public static SecurityEvent AccountDeleted(Guid userId) =>
        new(SecurityEventKind.AccountDeleted, userId);

    /// <summary>
    /// An account was removed by its own holder, the credentials proven again on the way out. Its id
    /// outlives it here, and in the rows that named it.
    /// </summary>
    public static SecurityEvent AccountSelfDeleted(Guid userId) =>
        new(SecurityEventKind.AccountSelfDeleted, userId);

    /// <summary>A first code was verified, so the account now demands one at every sign-in.</summary>
    public static SecurityEvent TwoFactorEnabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorEnabled, userId);

    /// <summary>The holder turned the second factor off, having proven the current password again.</summary>
    public static SecurityEvent TwoFactorDisabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorDisabled, userId);

    /// <summary>A live challenge was answered with a code that did not match it.</summary>
    public static SecurityEvent TwoFactorChallengeFailed(Guid userId) =>
        new(SecurityEventKind.TwoFactorChallengeFailed, userId);

    /// <summary>A sign-in finished on a recovery code, one of the ten now being spent.</summary>
    public static SecurityEvent RecoveryCodeRedeemed(Guid userId) =>
        new(SecurityEventKind.RecoveryCodeRedeemed, userId);

    /// <summary>
    /// The second factor was stripped on an administrator's authority, with no credential of the
    /// account's own having been proven.
    /// </summary>
    public static SecurityEvent TwoFactorDisabledByAdministrator(Guid userId) =>
        new(SecurityEventKind.TwoFactorDisabledByAdministrator, userId);

    /// <summary>
    /// A passkey was registered on the account, the holder having proven the session was their
    /// own on the way.
    /// </summary>
    public static SecurityEvent PasskeyRegistered(Guid userId) =>
        new(SecurityEventKind.PasskeyRegistered, userId);

    /// <summary>A passkey the account held was removed by its holder.</summary>
    public static SecurityEvent PasskeyRemoved(Guid userId) =>
        new(SecurityEventKind.PasskeyRemoved, userId);

    /// <summary>A sign-in completed on a passkey alone, a session starting with it.</summary>
    public static SecurityEvent PasskeySignInSucceeded(Guid userId) =>
        new(SecurityEventKind.PasskeySignInSucceeded, userId);

    /// <summary>
    /// A passkey sign-in was refused. <paramref name="userId"/> is <c>null</c> when the ceremony
    /// failed before an account was identified, for the same reason
    /// <see cref="AuthenticationFailed"/> carries none: what named the account is the one thing
    /// this log will not hold.
    /// </summary>
    public static SecurityEvent PasskeySignInFailed(Guid? userId) =>
        new(SecurityEventKind.PasskeySignInFailed, userId);
}

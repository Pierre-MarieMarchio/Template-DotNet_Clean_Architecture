using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;

namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

public sealed record SecurityEvent(
    SecurityEventKind Kind,
    Guid? UserId,
    CredentialCheckStatus? Status = null,
    string? Role = null)
{
    public static SecurityEvent LoginSucceeded(Guid userId) =>
        new(SecurityEventKind.LoginSucceeded, userId);

    public static SecurityEvent AuthenticationFailed(Guid? userId, CredentialCheckStatus outcome) =>
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

    public static SecurityEvent AccountLockedByAdministrator(Guid userId) =>
        new(SecurityEventKind.AccountLockedByAdministrator, userId);

    public static SecurityEvent AccountUnlockedByAdministrator(Guid userId) =>
        new(SecurityEventKind.AccountUnlockedByAdministrator, userId);

    public static SecurityEvent RoleGranted(Guid userId, string role) =>
        new(SecurityEventKind.RoleGranted, userId, Role: role);

    public static SecurityEvent RoleRevoked(Guid userId, string role) =>
        new(SecurityEventKind.RoleRevoked, userId, Role: role);

    public static SecurityEvent AccountDeleted(Guid userId) =>
        new(SecurityEventKind.AccountDeleted, userId);

    public static SecurityEvent AccountSelfDeleted(Guid userId) =>
        new(SecurityEventKind.AccountSelfDeleted, userId);

    public static SecurityEvent TwoFactorEnabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorEnabled, userId);

    public static SecurityEvent TwoFactorDisabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorDisabled, userId);

    public static SecurityEvent TwoFactorChallengeFailed(Guid userId) =>
        new(SecurityEventKind.TwoFactorChallengeFailed, userId);

    public static SecurityEvent RecoveryCodeRedeemed(Guid userId) =>
        new(SecurityEventKind.RecoveryCodeRedeemed, userId);

    public static SecurityEvent TwoFactorDisabledByAdministrator(Guid userId) =>
        new(SecurityEventKind.TwoFactorDisabledByAdministrator, userId);

    public static SecurityEvent PasskeyRegistered(Guid userId) =>
        new(SecurityEventKind.PasskeyRegistered, userId);

    public static SecurityEvent PasskeyRemoved(Guid userId) =>
        new(SecurityEventKind.PasskeyRemoved, userId);

    public static SecurityEvent PasskeySignInSucceeded(Guid userId) =>
        new(SecurityEventKind.PasskeySignInSucceeded, userId);

    public static SecurityEvent PasskeySignInFailed(Guid? userId) =>
        new(SecurityEventKind.PasskeySignInFailed, userId);
    //#if (accessKeys)

    public static SecurityEvent AccessKeyCreated(Guid userId) =>
        new(SecurityEventKind.AccessKeyCreated, userId);

    public static SecurityEvent AccessKeyRevoked(Guid userId) =>
        new(SecurityEventKind.AccessKeyRevoked, userId);
    //#endif
}

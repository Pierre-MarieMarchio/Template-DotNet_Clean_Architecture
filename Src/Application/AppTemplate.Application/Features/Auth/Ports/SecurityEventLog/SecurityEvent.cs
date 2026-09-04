using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

/// <summary>
/// One fact for <see cref="ISecurityEventLog"/> to record. <see cref="UserId"/> is <c>null</c> only
/// for a failed authentication against an address that named no account at all — there is nothing
/// to identify that attempt by that is not the address itself. <see cref="Outcome"/> is set only for
/// <see cref="SecurityEventKind.AuthenticationFailed"/>. <see cref="Role"/> is set only for
/// <see cref="SecurityEventKind.RoleGranted"/> and <see cref="SecurityEventKind.RoleRevoked"/>.
/// </summary>
public sealed record SecurityEvent(
    SecurityEventKind Kind,
    Guid? UserId,
    CredentialCheckOutcome? Outcome = null,
    string? Role = null)
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

    public static SecurityEvent TwoFactorEnabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorEnabled, userId);

    public static SecurityEvent TwoFactorDisabled(Guid userId) =>
        new(SecurityEventKind.TwoFactorDisabled, userId);

    public static SecurityEvent TwoFactorChallengeFailed(Guid userId) =>
        new(SecurityEventKind.TwoFactorChallengeFailed, userId);

    public static SecurityEvent RecoveryCodeRedeemed(Guid userId) =>
        new(SecurityEventKind.RecoveryCodeRedeemed, userId);
}

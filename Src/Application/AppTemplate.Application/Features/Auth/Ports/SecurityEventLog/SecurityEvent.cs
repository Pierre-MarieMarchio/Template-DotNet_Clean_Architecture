using AppTemplate.Application.Features.Auth.Ports.UserAccounts;

namespace AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

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

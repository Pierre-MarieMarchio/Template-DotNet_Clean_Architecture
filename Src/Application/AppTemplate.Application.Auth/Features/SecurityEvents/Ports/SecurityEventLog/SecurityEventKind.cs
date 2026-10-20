namespace AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;

public enum SecurityEventKind
{
    LoginSucceeded,

    AuthenticationFailed,

    AccountLockedOut,

    Registered,

    LoggedOut,

    RefreshTokenRevoked,

    RefreshTokenReplayDetected,

    SecurityStampRotated,

    AccountLockedByAdministrator,

    AccountUnlockedByAdministrator,

    RoleGranted,

    RoleRevoked,

    AccountDeleted,

    AccountSelfDeleted,

    TwoFactorEnabled,

    TwoFactorDisabled,

    TwoFactorChallengeFailed,

    RecoveryCodeRedeemed,

    TwoFactorDisabledByAdministrator,

    PasskeyRegistered,

    PasskeyRemoved,

    PasskeySignInSucceeded,

    PasskeySignInFailed,
}

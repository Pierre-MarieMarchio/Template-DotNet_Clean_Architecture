using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Logs;

/// <summary>
/// Writes every <see cref="ISecurityEventLog"/> event as a structured log entry. The host's
/// <c>AddJsonConsole</c> already emits scopes and named parameters as JSON fields, so nothing here
/// formats a message for a human — <see cref="LoggerMessage"/> delegates carry the user id as its
/// own field rather than interpolated into text.
/// </summary>
internal sealed partial class SecurityEventLog(ILogger<SecurityEventLog> logger) : ISecurityEventLog
{
    public void Record(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        switch (securityEvent.Kind)
        {
            case SecurityEventKind.LoginSucceeded:
                LogLoginSucceeded(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.AuthenticationFailed:
                LogAuthenticationFailed(logger, securityEvent.UserId, securityEvent.Status);
                break;

            case SecurityEventKind.AccountLockedOut:
                LogAccountLockedOut(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.Registered:
                LogRegistered(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.LoggedOut:
                LogLoggedOut(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.RefreshTokenRevoked:
                LogRefreshTokenRevoked(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.RefreshTokenReplayDetected:
                LogRefreshTokenReplayDetected(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.SecurityStampRotated:
                LogSecurityStampRotated(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.AccountLockedByAdministrator:
                LogAccountLockedByAdministrator(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.AccountUnlockedByAdministrator:
                LogAccountUnlockedByAdministrator(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.RoleGranted:
                LogRoleGranted(logger, securityEvent.UserId, securityEvent.Role);
                break;

            case SecurityEventKind.RoleRevoked:
                LogRoleRevoked(logger, securityEvent.UserId, securityEvent.Role);
                break;

            case SecurityEventKind.AccountDeleted:
                LogAccountDeleted(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.TwoFactorEnabled:
                LogTwoFactorEnabled(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.TwoFactorDisabled:
                LogTwoFactorDisabled(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.TwoFactorChallengeFailed:
                LogTwoFactorChallengeFailed(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.RecoveryCodeRedeemed:
                LogRecoveryCodeRedeemed(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.TwoFactorDisabledByAdministrator:
                LogTwoFactorDisabledByAdministrator(logger, securityEvent.UserId);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(securityEvent),
                    securityEvent.Kind,
                    $"Unknown {nameof(SecurityEventKind)}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Login succeeded for user {UserId}.")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid? userId);

    /// <summary>
    /// The field is <c>{Status}</c> because what it carries is a <see cref="CredentialCheckStatus"/>.
    /// In this repository's vocabulary (CONTRIBUTING.md, Naming) an <c>…Outcome</c> is the record an
    /// operation hands back and a <c>…Status</c> is the closed enum of ways it went, so the old
    /// <c>{Outcome}</c> named the wrong one of the two in the JSON an operator queries.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Authentication failed for user {UserId} with status {Status}.")]
    private static partial void LogAuthenticationFailed(
        ILogger logger,
        Guid? userId,
        CredentialCheckStatus? status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} was locked out.")]
    private static partial void LogAccountLockedOut(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered.")]
    private static partial void LogRegistered(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} logged out.")]
    private static partial void LogLoggedOut(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Every refresh-token grant for user {UserId} was revoked.")]
    private static partial void LogRefreshTokenRevoked(ILogger logger, Guid? userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A consumed refresh token was replayed for user {UserId}.")]
    private static partial void LogRefreshTokenReplayDetected(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "The security stamp for user {UserId} was rotated.")]
    private static partial void LogSecurityStampRotated(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An administrator locked out user {UserId}.")]
    private static partial void LogAccountLockedByAdministrator(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "An administrator lifted the lockout on user {UserId}.")]
    private static partial void LogAccountUnlockedByAdministrator(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "An administrator granted role {Role} to user {UserId}.")]
    private static partial void LogRoleGranted(ILogger logger, Guid? userId, string? role);

    [LoggerMessage(Level = LogLevel.Information, Message = "An administrator revoked role {Role} from user {UserId}.")]
    private static partial void LogRoleRevoked(ILogger logger, Guid? userId, string? role);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An administrator deleted user {UserId}.")]
    private static partial void LogAccountDeleted(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Two-factor sign-in was armed for user {UserId}.")]
    private static partial void LogTwoFactorEnabled(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Two-factor sign-in was turned off for user {UserId}.")]
    private static partial void LogTwoFactorDisabled(ILogger logger, Guid? userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A two-factor challenge for user {UserId} was presented with a wrong code.")]
    private static partial void LogTwoFactorChallengeFailed(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A recovery code was redeemed for user {UserId}.")]
    private static partial void LogRecoveryCodeRedeemed(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An administrator disabled two-factor sign-in for user {UserId}.")]
    private static partial void LogTwoFactorDisabledByAdministrator(ILogger logger, Guid? userId);
}

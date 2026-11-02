using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Services;

internal sealed partial class SecurityEventLog(
    ILogger<SecurityEventLog> logger,
    DbContextOptions<AuthDbContext> contextOptions,
    AuthDbContext context,
    ISecurityEventOriginProvider origin,
    IDateTimeProvider dateTimeProvider,
    IOptions<SecurityEventLogOptions> options) : ISecurityEventLog
{
    public void Record(SecurityEvent securityEvent)
    {
        ArgumentNullException.ThrowIfNull(securityEvent);

        WriteLogEntry(securityEvent);
        WriteRow(securityEvent);
    }

    public async Task<int> PurgeExpiredAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        var cutoff = asOf.AddDays(-options.Value.RetentionInDays);
        int batchSize = options.Value.PurgeBatchSize;
        int purged = 0;
        int deleted;

        do
        {
            deleted = await context.SecurityEvents
                .Where(securityEvent => securityEvent.OccurredAt < cutoff)
                .OrderBy(securityEvent => securityEvent.OccurredAt)
                .Take(batchSize)
                .ExecuteDeleteAsync(cancellationToken);

            purged += deleted;
        }
        while (deleted == batchSize);

        return purged;
    }

    private void WriteRow(SecurityEvent securityEvent)
    {
        try
        {
            using var own = new AuthDbContext(contextOptions);

            own.SecurityEvents.Add(new SecurityEventRecord
            {
                UserId = securityEvent.UserId,
                Kind = securityEvent.Kind,
                OccurredAt = dateTimeProvider.UtcNow,
                IpAddress = Truncate(origin.IpAddress, SecurityEventConfiguration.IpAddressLength),
                UserAgent = Truncate(origin.UserAgent, SecurityEventConfiguration.UserAgentLength),
                Detail = Truncate(DetailOf(securityEvent), SecurityEventConfiguration.DetailLength),
            });

            own.SaveChanges();
        }
        catch (Exception exception)
        {
            LogRowNotWritten(logger, exception, securityEvent.Kind, securityEvent.UserId);
        }
    }

    private static string? DetailOf(SecurityEvent securityEvent) =>
        securityEvent.Status?.ToString() ?? securityEvent.Role;

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private void WriteLogEntry(SecurityEvent securityEvent)
    {
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

            case SecurityEventKind.AccountSelfDeleted:
                LogAccountSelfDeleted(logger, securityEvent.UserId);
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

            case SecurityEventKind.PasskeyRegistered:
                LogPasskeyRegistered(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.PasskeyRemoved:
                LogPasskeyRemoved(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.PasskeySignInSucceeded:
                LogPasskeySignInSucceeded(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.PasskeySignInFailed:
                LogPasskeySignInFailed(logger, securityEvent.UserId);
                break;

            //#if (accessKeys)
            case SecurityEventKind.AccessKeyCreated:
                LogAccessKeyCreated(logger, securityEvent.UserId);
                break;

            case SecurityEventKind.AccessKeyRevoked:
                LogAccessKeyRevoked(logger, securityEvent.UserId);
                break;

            //#endif
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(securityEvent),
                    securityEvent.Kind,
                    $"Unknown {nameof(SecurityEventKind)}.");
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Security event {Kind} for user {UserId} was logged but could not be stored.")]
    private static partial void LogRowNotWritten(
        ILogger logger,
        Exception exception,
        SecurityEventKind kind,
        Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Login succeeded for user {UserId}.")]
    private static partial void LogLoginSucceeded(ILogger logger, Guid? userId);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} deleted their own account.")]
    private static partial void LogAccountSelfDeleted(ILogger logger, Guid? userId);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "A passkey was registered for user {UserId}.")]
    private static partial void LogPasskeyRegistered(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "A passkey of user {UserId} was removed.")]
    private static partial void LogPasskeyRemoved(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} signed in with a passkey.")]
    private static partial void LogPasskeySignInSucceeded(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A passkey sign-in was refused for user {UserId}.")]
    private static partial void LogPasskeySignInFailed(ILogger logger, Guid? userId);
    //#if (accessKeys)

    [LoggerMessage(Level = LogLevel.Information, Message = "An access key was created for user {UserId}.")]
    private static partial void LogAccessKeyCreated(ILogger logger, Guid? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "An access key of user {UserId} was revoked.")]
    private static partial void LogAccessKeyRevoked(ILogger logger, Guid? userId);
    //#endif
}

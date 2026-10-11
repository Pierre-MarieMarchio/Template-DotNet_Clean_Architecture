using AppTemplate.Application.Auth.Features.Auth.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Configurations;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth.Features.Auth.Logs;

/// <summary>
/// Keeps every <see cref="ISecurityEventLog"/> event twice: as a structured log entry, and as a row
/// of the <c>identity.SecurityEvents</c> table the account holder and an administrator can read back.
/// <para>
/// <b>The log entry.</b> The host's <c>AddJsonConsole</c> already emits scopes and named parameters
/// as JSON fields, so nothing here formats a message for a human — <see cref="LoggerMessage"/>
/// delegates carry the user id as its own field rather than interpolated into text. It is written
/// first, so an event the table misses is still in the log.
/// </para>
/// <para>
/// <b>The row, and what is guaranteed about it.</b> It is written in its own short transaction, on a
/// context of its own, at the moment the event is recorded — which is after the action it describes
/// has done its work, since every caller records the decision it already took. It is deliberately not
/// part of the action's transaction: several events have none (a refused sign-in changes nothing), a
/// shared context would flush whatever the caller still had pending, and a row joined to the action
/// would vanish with a rollback that is itself worth recording. If the write fails, the failure is
/// logged at <c>Error</c> with the event's kind and user id, and the action's outcome is left exactly
/// as it was: availability over completeness. A sign-in does not fail because its audit row could not
/// be stored, and the trail can therefore have gaps the log does not.
/// </para>
/// <para>
/// The write is synchronous because <see cref="ISecurityEventLog.Record"/> is: one small insert, on
/// a pooled connection, after the response is decided.
/// </para>
/// </summary>
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

        // Oldest first, a batch per statement: each delete is its own short transaction, so a large
        // backlog never holds one lock over the whole table.
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

    /// <summary>
    /// A context of its own, so the insert commits alone: nothing the caller's scoped context still
    /// tracks is flushed with it, and no transaction the caller opened can take it down.
    /// </summary>
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
            // Deliberately every exception: see the class summary. The event itself is already in the
            // log above; this line says the table is missing it.
            LogRowNotWritten(logger, exception, securityEvent.Kind, securityEvent.UserId);
        }
    }

    private static string? DetailOf(SecurityEvent securityEvent) =>
        securityEvent.Status?.ToString() ?? securityEvent.Role;

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= maxLength ? value : value[..maxLength];

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

    /// <summary>
    /// The field is <c>{Status}</c> because what it carries is a <see cref="CredentialCheckStatus"/>.
    /// In this repository's vocabulary (docs/CONVENTIONS.md, Naming) an <c>…Outcome</c> is the record an
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
}

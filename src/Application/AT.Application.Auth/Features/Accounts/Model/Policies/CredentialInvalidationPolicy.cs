using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.Features.Accounts.Model.Policies;

public static class CredentialInvalidationPolicy
{
    public static async Task InvalidateAsync(
        ISessionTokensService sessionTokens,
        ISecurityEventLog securityEventLog,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionTokens);
        ArgumentNullException.ThrowIfNull(securityEventLog);

        await sessionTokens.RevokeAllRefreshTokensAsync(userId, cancellationToken: cancellationToken);
        await securityEventLog.RecordAsync(SecurityEvent.SecurityStampRotated(userId));
    }

    public static async Task InvalidateOtherSessionsAsync(
        ISessionTokensService sessionTokens,
        ISecurityEventLog securityEventLog,
        Guid userId,
        Guid? currentSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionTokens);
        ArgumentNullException.ThrowIfNull(securityEventLog);

        await sessionTokens.RevokeAllRefreshTokensAsync(userId, currentSessionId, cancellationToken);
        await securityEventLog.RecordAsync(SecurityEvent.SecurityStampRotated(userId));
    }
}

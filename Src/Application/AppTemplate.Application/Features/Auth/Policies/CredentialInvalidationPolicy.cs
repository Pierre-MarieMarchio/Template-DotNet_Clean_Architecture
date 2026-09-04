using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.SecurityEventLog;

namespace AppTemplate.Application.Features.Auth.Policies;

/// <summary>
/// What a use case runs immediately after rotating a user's security stamp.
/// <para>
/// The stamp lives inside ASP.NET Identity and its rotation already fails every access token in
/// circulation — that part needs nothing further. Refresh tokens are issued by this codebase, not
/// by Identity, and survive the rotation untouched: left alone, a stolen or still-valid refresh
/// token would keep minting fresh access tokens forever. Revoking every grant and recording the
/// fact are therefore two steps every stamp rotation owes, called here so a future feature that
/// rotates a stamp cannot forget one.
/// </para>
/// </summary>
public static class CredentialInvalidationPolicy
{
    public static async Task InvalidateAsync(
        IRefreshTokenGrantsService refreshTokens,
        ISecurityEventLog securityEventLog,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refreshTokens);
        ArgumentNullException.ThrowIfNull(securityEventLog);

        await refreshTokens.RevokeAllForUserAsync(userId, cancellationToken);
        securityEventLog.Record(SecurityEvent.SecurityStampRotated(userId));
    }
}

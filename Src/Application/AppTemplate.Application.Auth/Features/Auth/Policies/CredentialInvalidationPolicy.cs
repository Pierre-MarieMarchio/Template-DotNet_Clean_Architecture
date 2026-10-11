using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;

namespace AppTemplate.Application.Auth.Features.Auth.Policies;

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
/// <para>
/// Two forms, chosen by who asked for the change. When the account's owner changed it from a
/// signed-in session — a new password, a second factor armed or dropped — that session already
/// proved the new posture and <see cref="InvalidateOtherSessionsAsync"/> spares it: its access token
/// still fails on the rotated stamp, but its refresh token mints a new one. Everything else — a
/// reset by mailed link, an administrator's action, a confirmed email change — ends every session
/// through <see cref="InvalidateAsync"/>.
/// </para>
/// </summary>
public static class CredentialInvalidationPolicy
{
    /// <summary>
    /// Revokes every refresh-token grant the user holds and records the rotation, in that order.
    /// Called after the stamp has already been rotated, never instead of rotating it.
    /// </summary>
    /// <param name="userId">The account whose stamp was just rotated.</param>
    public static async Task InvalidateAsync(
        ISessionTokensService sessionTokens,
        ISecurityEventLog securityEventLog,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionTokens);
        ArgumentNullException.ThrowIfNull(securityEventLog);

        await sessionTokens.RevokeAllRefreshTokensAsync(userId, cancellationToken: cancellationToken);
        securityEventLog.Record(SecurityEvent.SecurityStampRotated(userId));
    }

    /// <summary>
    /// Revokes every refresh-token grant the user holds outside <paramref name="currentSessionId"/>,
    /// and records the rotation. Called by the owner's own change, after the stamp was rotated.
    /// </summary>
    /// <param name="userId">The account whose stamp was just rotated.</param>
    /// <param name="currentSessionId">
    /// The session the change was made from, read from the caller's access token. <c>null</c> — a
    /// token issued before sessions were named in it — ends every session, as
    /// <see cref="InvalidateAsync"/> does: an unknown session is not one to spare.
    /// </param>
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
        securityEventLog.Record(SecurityEvent.SecurityStampRotated(userId));
    }
}

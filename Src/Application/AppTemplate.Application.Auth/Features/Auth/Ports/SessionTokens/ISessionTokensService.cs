namespace AppTemplate.Application.Auth.Features.Auth.Ports.SessionTokens;

/// <summary>
/// The tokens a signed-in session holds: a short-lived signed access token, and an opaque refresh
/// token whose grants this port issues, rotates, revokes and finally purges.
/// <para>
/// The raw token values are credentials, so a caller of this port can mint one. That is why the
/// adapter behind it is internal to its module and why nothing outside this vertical's use cases
/// takes a dependency on it.
/// </para>
/// <para>
/// <b>Every refresh-token operation commits the request's unit of work</b>, because a grant has to
/// be durable before its value is handed to a client. A caller that has staged other work in the
/// same request must expect that work to be committed too.
/// </para>
/// </summary>
public interface ISessionTokensService
{
    /// <summary>
    /// Reads the account's current claims and signs them, so a claim revoked between two issuances
    /// is gone from the next token. The token names <paramref name="sessionId"/> in its <c>sid</c>
    /// claim, which is how a later request tells which session it is made from.
    /// </summary>
    /// <param name="sessionId">The session the token is issued in: <see cref="IssuedRefreshToken.SessionId"/>
    /// of the refresh token issued or rotated beside it.</param>
    /// <exception cref="InvalidOperationException">No account has that id.</exception>
    Task<IssuedAccessToken> IssueAccessTokenAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a session and mints its first grant, for a caller whose credential has just been
    /// verified. Durable before it returns, since its value is about to leave the process.
    /// </summary>
    /// <param name="client">Recorded on the session so its owner can recognise it in a list.</param>
    Task<IssuedRefreshToken> IssueRefreshTokenAsync(
        Guid userId,
        SessionClient client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a presented refresh token and issues its successor in the same session, single-use:
    /// of two simultaneous presentations exactly one may be told it rotated. Presenting one that was
    /// already rotated while its session is still live means either a replay or a leak, so every
    /// session of that user is revoked and the request fails. A token of a session that was ended —
    /// signed out, revoked, or ended by a credential change — is refused without that alarm: it is
    /// what a client still holding it sends next, and nothing about it is suspicious. A revocation
    /// committed before or during a rotation wins: the session's successor, if one was being issued,
    /// never rotates.
    /// </summary>
    /// <param name="client">Recorded on the session as its latest use.</param>
    Task<RefreshTokenRotation> RotateRefreshTokenAsync(
        string presentedToken,
        SessionClient client,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session the presented refresh token belongs to. Silent when the token is unknown,
    /// so logging out cannot be used to probe for valid tokens.
    /// </summary>
    /// <returns>The id of the user the token belonged to, or <c>null</c> when the token was unknown.</returns>
    Task<Guid?> RevokeRefreshTokenAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every session of a user, or every one but <paramref name="exceptSessionId"/>: theft
    /// response, "sign out everywhere", and what a credential change owes the other sessions.
    /// </summary>
    /// <param name="exceptSessionId">A session to leave running, or <c>null</c> to end them all. An id
    /// naming no live session of this user spares nothing.</param>
    Task RevokeAllRefreshTokensAsync(
        Guid userId,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every grant whose retention window has passed as of <paramref name="asOf"/>. How long
    /// an expired grant is kept is the adapter's configuration: keeping it a while is what makes a
    /// replayed, already-rotated token detectable instead of merely unknown.
    /// </summary>
    /// <returns>How many grants were deleted.</returns>
    Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

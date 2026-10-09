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
    /// is gone from the next token.
    /// </summary>
    /// <exception cref="InvalidOperationException">No account has that id.</exception>
    Task<IssuedAccessToken> IssueAccessTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mints the first grant of a session, for a caller whose credential has just been verified.
    /// Durable before it returns, since its value is about to leave the process.
    /// </summary>
    Task<IssuedRefreshToken> IssueRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a presented refresh token and issues its successor, single-use: of two simultaneous
    /// presentations exactly one may be told it rotated. Presenting one that was already rotated or
    /// revoked means either a replay or a leak, so the entire family for that user is revoked and
    /// the request fails.
    /// </summary>
    Task<RefreshTokenRotation> RotateRefreshTokenAsync(
        string presentedToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the presented refresh token if it is still active. Silent when the token is unknown,
    /// so logging out cannot be used to probe for valid tokens.
    /// </summary>
    /// <returns>The id of the user the token belonged to, or <c>null</c> when the token was unknown.</returns>
    Task<Guid?> RevokeRefreshTokenAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Kills every live grant for a user: theft response, and the hook for "sign out everywhere".</summary>
    Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every grant whose retention window has passed as of <paramref name="asOf"/>. How long
    /// an expired grant is kept is the adapter's configuration: keeping it a while is what makes a
    /// replayed, already-rotated token detectable instead of merely unknown.
    /// </summary>
    /// <returns>How many grants were deleted.</returns>
    Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

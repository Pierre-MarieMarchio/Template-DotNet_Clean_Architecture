namespace AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;

/// <summary>
/// Issues, rotates and revokes refresh tokens.
/// <para>
/// The raw token value is a long-lived credential, so a caller of this port can mint one. That is
/// why the adapter behind it is internal to its module and why nothing outside this vertical's use
/// cases takes a dependency on it.
/// </para>
/// <para>
/// <b>Every method here commits the request's unit of work</b>, because a token grant has to be
/// durable before its value is handed to a client. A caller that has staged other work in the same
/// request must expect that work to be committed too.
/// </para>
/// </summary>
public interface IRefreshTokenGrants
{
    Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a presented token and issues its successor, single-use: of two simultaneous
    /// presentations exactly one may be told it rotated. Presenting one that was already rotated or
    /// revoked means either a replay or a leak, so the entire family for that user is revoked and
    /// the request fails.
    /// </summary>
    Task<RefreshTokenRotation> RotateAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the presented token if it is still active. Silent when the token is unknown, so
    /// logging out cannot be used to probe for valid tokens.
    /// </summary>
    /// <returns>The id of the user the token belonged to, or <c>null</c> when the token was unknown.</returns>
    Task<Guid?> RevokeAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Kills every live grant for a user: theft response, and the hook for "sign out everywhere".</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}

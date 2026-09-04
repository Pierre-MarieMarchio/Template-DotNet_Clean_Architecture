namespace AppTemplate.Application.Features.Auth.Ports;

/// <summary>
/// Mints access tokens, and nothing else. Separate from <see cref="IRefreshTokenGrants"/> because a
/// refresh token is opaque server-side state and an access token is a signed assertion: an
/// implementer of one has no use for the other's machinery.
/// </summary>
public interface IAccessTokenIssuer
{
    /// <summary>
    /// Reads the account's current claims and signs them, so a claim revoked between two issuances
    /// is gone from the next token.
    /// </summary>
    /// <exception cref="InvalidOperationException">No account has that id.</exception>
    Task<IssuedAccessToken> IssueAsync(Guid userId, CancellationToken cancellationToken = default);
}

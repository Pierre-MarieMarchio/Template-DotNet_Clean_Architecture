namespace AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;

/// <summary>
/// An account's sessions as their owner sees and ends them. A session is one sign-in's chain of
/// refresh tokens: it starts when <c>ISessionTokensService</c> issues the first one and every
/// rotation stays in it. That port handles the credentials; this one never touches a token value.
/// </summary>
public interface ISessionsService
{
    /// <summary>
    /// The account's sessions that are neither revoked nor past the expiry of their newest refresh
    /// token, most recently used first.
    /// </summary>
    Task<IReadOnlyList<ActiveSession>> ListActiveAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends one of the account's sessions: no refresh token of it rotates again, including one a
    /// concurrent rotation is issuing at this moment. Idempotent: ending a session already ended
    /// succeeds and changes nothing. Commits before it returns.
    /// </summary>
    /// <returns><c>false</c> when the account has no session with that id, whether no such session exists or it belongs to somebody else: the caller is not told which.</returns>
    Task<bool> RevokeAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}

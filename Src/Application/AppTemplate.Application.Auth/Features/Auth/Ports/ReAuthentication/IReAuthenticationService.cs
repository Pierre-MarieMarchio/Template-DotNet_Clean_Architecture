namespace AppTemplate.Application.Auth.Features.Auth.Ports.ReAuthentication;

/// <summary>
/// The primitives behind re-authentication: proving that this session still holds the account's own
/// credentials, for an operation that is not reversible once done. Deliberately primitive, not one
/// "confirm me" call — <b>which</b> evidence an operation demands is the use case's decision, where
/// a unit test can enumerate the cases; this port answers only "is this the credential?" for the
/// evidence it is handed. The account's existence is not this port's question: a caller that
/// reached it already holds an access token naming the account. Cancellation is observed on entry
/// only, for the reason given on <see cref="Accounts.IAccountsService"/>.
/// </summary>
public interface IReAuthenticationService
{
    /// <summary>
    /// Verifies a password against the account's stored hash. No lockout is counted and no security
    /// stamp is rotated: a failed proof must not lock an account its holder is signed into, and a
    /// passed one must not end every session the way a password <em>change</em> does.
    /// </summary>
    /// <returns>
    /// Whether the password is the account's. An account that no longer exists answers
    /// <c>false</c>, like a wrong password: the caller has already established it exists.
    /// </returns>
    Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a code against the account's armed second factor — the authenticator app's current
    /// code first, a recovery code second, exactly as a sign-in challenge accepts. A matched
    /// recovery code is spent, as it is at sign-in.
    /// </summary>
    /// <returns>
    /// Whether the code is one the armed factor accepts. An account that no longer exists, or has
    /// the factor unarmed, answers <c>false</c>: the caller has already established both.
    /// </returns>
    Task<bool> VerifyTwoFactorCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}

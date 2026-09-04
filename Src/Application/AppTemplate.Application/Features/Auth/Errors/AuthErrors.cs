using AppTemplate.Application.Common;

namespace AppTemplate.Application.Features.Auth.Errors;

/// <summary>
/// The failures this vertical is allowed to disclose. Each is a single, stable, deliberately
/// uninformative value, and several of them are the same value returned for causes the caller must
/// not be able to tell apart.
/// </summary>
public static class AuthErrors
{
    /// <summary>
    /// One error for every login failure: unknown address, wrong password, unconfirmed email,
    /// locked-out account. Even the lockout is not disclosed, since saying so confirms the account.
    /// </summary>
    public static Error InvalidCredentials { get; } = Error.Unauthorized(
        "auth.login.invalidCredentials",
        "Email or password is incorrect.");

    /// <summary>
    /// Covers unknown, expired, revoked and replayed refresh tokens alike. A replay additionally
    /// revokes the whole family, but the response is identical.
    /// </summary>
    public static Error InvalidRefreshToken { get; } = Error.Unauthorized(
        "auth.refreshToken.invalid",
        "The refresh token is invalid or has expired.");

    /// <summary>
    /// Identical whether the address is unknown or the token is wrong, so the endpoint cannot be
    /// used to test which addresses are registered.
    /// </summary>
    public static Error InvalidEmailConfirmation { get; } = Error.Validation(
        "auth.confirmEmail.invalid",
        "The confirmation link is invalid or has expired.");

    /// <summary>
    /// Registration cannot fully hide that an address is taken, but it does not have to say so:
    /// the message is neutral and identical for a duplicate username and a duplicate email.
    /// </summary>
    public static Error RegistrationConflict { get; } = Error.Conflict(
        "auth.register.unavailable",
        "That username or email address cannot be used.");

    /// <summary>
    /// <paramref name="message"/> describes the submitted values — password policy, allowed
    /// characters, format — so it is safe, and useful, to return verbatim.
    /// </summary>
    public static Error RegistrationRejected(string message) =>
        Error.Validation("auth.register.rejected", message);
}

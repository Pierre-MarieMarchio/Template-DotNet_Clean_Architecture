using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Sessions.Model.Errors;

/// <summary>
/// The failures signing in and holding a session are allowed to disclose: single, stable,
/// deliberately uninformative values, the same one returned for causes the caller must not be able
/// to tell apart.
/// </summary>
public static class SessionsErrors
{
    /// <summary>
    /// One error for every login failure — unknown address, wrong password, unconfirmed email,
    /// locked-out account. Even the lockout is not disclosed, since saying so confirms the account.
    /// </summary>
    public static Error InvalidCredentials { get; } = Error.Unauthorized(
        "auth.login.invalidCredentials",
        "Email or password is incorrect.");

    /// <summary>
    /// Covers unknown, expired, revoked and replayed refresh tokens alike; a replay additionally
    /// revokes the whole family, but the response is identical.
    /// </summary>
    public static Error InvalidRefreshToken { get; } = Error.Unauthorized(
        "auth.refreshToken.invalid",
        "The refresh token is invalid or has expired.");

    /// <summary>
    /// The caller named a session that is not one of theirs; also the answer for another account's
    /// session, so the route discloses nothing about which session ids exist.
    /// </summary>
    public static Error NoSuchSession { get; } = Error.NotFound(
        "auth.session.notFound",
        "No such session.");
}

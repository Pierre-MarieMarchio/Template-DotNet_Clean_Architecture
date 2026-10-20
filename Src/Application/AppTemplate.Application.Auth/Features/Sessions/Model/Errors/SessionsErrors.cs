using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Auth.Features.Sessions.Model.Errors;

public static class SessionsErrors
{
    public static Error InvalidCredentials { get; } = Error.Unauthorized(
        "auth.login.invalidCredentials",
        "Email or password is incorrect.");

    public static Error InvalidRefreshToken { get; } = Error.Unauthorized(
        "auth.refreshToken.invalid",
        "The refresh token is invalid or has expired.");

    public static Error NoSuchSession { get; } = Error.NotFound(
        "auth.session.notFound",
        "No such session.");
}

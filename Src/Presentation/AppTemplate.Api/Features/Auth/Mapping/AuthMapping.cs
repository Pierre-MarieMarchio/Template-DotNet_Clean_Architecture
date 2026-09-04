using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Common;
using ApplicationCurrentUserResponse = AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser.CurrentUserResponse;
using ApplicationLoginOutcome = AppTemplate.Application.Features.Auth.UseCases.Commands.Login.LoginOutcome;
using ApplicationRefreshAccessTokenResponse = AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken.RefreshAccessTokenResponse;
using ApplicationRegisterResponse = AppTemplate.Application.Features.Auth.UseCases.Commands.Register.RegisterResponse;

namespace AppTemplate.Api.Features.Auth.Mapping;

/// <summary>
/// Projects use-case output onto the HTTP contract, by hand — <c>docs/adr/0011</c> rejects mapping
/// libraries: a projection the compiler cannot check is a runtime failure waiting for a rename.
/// </summary>
/// <remarks>
/// Each lift tests <see cref="Result.IsFailure"/> on its own line before touching
/// <c>Result{T}.Value</c>, which throws on a failure.
/// </remarks>
internal static class AuthMapping
{
    public static Result<RegisterResponse> ToRegisterResponse(Result<ApplicationRegisterResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<RegisterResponse>();
        }

        return new RegisterResponse(result.Value.UserName, result.Value.Email, result.Value.ConfirmationEmailSent);
    }

    public static Result<LoginResponse> ToLoginResponse(Result<ApplicationLoginOutcome> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<LoginResponse>();
        }

        return result.Value switch
        {
            ApplicationLoginOutcome.Authenticated authenticated => new LoginResponse.Authenticated(
                new TokenResponse(
                    authenticated.AccessToken,
                    authenticated.AccessTokenExpiresAt,
                    authenticated.RefreshToken,
                    authenticated.RefreshTokenExpiresAt)),
            ApplicationLoginOutcome.TwoFactorRequired twoFactor =>
                new LoginResponse.TwoFactorRequired(twoFactor.ChallengeToken),

            // A branch added to the hierarchy without one here would otherwise be served as a
            // success with no body at all.
            _ => throw new NotSupportedException(
                $"'{result.Value.GetType().Name}' has no HTTP contract: add a branch to LoginResponse."),
        };
    }

    public static Result<TokenResponse> ToTokenResponse(Result<ApplicationRefreshAccessTokenResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<TokenResponse>();
        }

        return new TokenResponse(
            result.Value.AccessToken,
            result.Value.AccessTokenExpiresAt,
            result.Value.RefreshToken,
            result.Value.RefreshTokenExpiresAt);
    }

    public static Result<CurrentUserResponse> ToCurrentUserResponse(
        Result<ApplicationCurrentUserResponse> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsFailure)
        {
            return result.To<CurrentUserResponse>();
        }

        return new CurrentUserResponse(
            result.Value.UserId,
            result.Value.UserName,
            result.Value.Email,
            result.Value.EmailConfirmed,
            result.Value.Roles,
            result.Value.CreatedAt,
            result.Value.TwoFactorEnabled);
    }
}

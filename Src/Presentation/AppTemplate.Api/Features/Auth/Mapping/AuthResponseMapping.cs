using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Login;
using AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Features.Auth.UseCases.Commands.Register;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.Features.Auth.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser;

namespace AppTemplate.Api.Features.Auth.Mapping;

/// <summary>
/// Projects use-case output onto the HTTP contract, by hand — this repository rejects mapping
/// libraries: a projection the compiler cannot check is a runtime failure waiting for a rename.
/// </summary>
/// <remarks>
/// Each lift tests <see cref="Result.IsFailure"/> on its own line before touching
/// <c>Result{T}.Value</c>, which throws on a failure.
/// </remarks>
internal static class AuthResponseMapping
{
    public static Result<RegisterResponse> ToRegisterResponse(Result<RegisterOutcome> result) =>
        result.Map(value => new RegisterResponse(value.UserName, value.Email, value.ConfirmationEmailSent));

    public static Result<LoginResponse> ToLoginResponse(Result<LoginOutcome> result) =>
        result.Map<LoginOutcome, LoginResponse>(value => value switch
        {
            LoginOutcome.Authenticated authenticated => new LoginResponse.Authenticated(
                new TokenResponse(
                    authenticated.AccessToken,
                    authenticated.AccessTokenExpiresAt,
                    authenticated.RefreshToken,
                    authenticated.RefreshTokenExpiresAt)),
            LoginOutcome.TwoFactorRequired twoFactor =>
                new LoginResponse.TwoFactorRequired(twoFactor.ChallengeToken),

            // A branch added to the hierarchy without one here would otherwise be served as a
            // success with no body at all.
            _ => throw new NotSupportedException(
                $"'{value.GetType().Name}' has no HTTP contract: add a branch to LoginResponse."),
        });

    /// <summary>
    /// The external sign-in's own two branches. Written out rather than routed through
    /// <see cref="ToLoginResponse"/>: the outcomes are two closed hierarchies that happen to have the
    /// same shape today, and a conversion between them would make either one unable to grow a branch
    /// without the other.
    /// </summary>
    public static Result<ExternalLoginResponse> ToExternalLoginResponse(
        Result<SignInWithExternalProviderOutcome> result) =>
        result.Map<SignInWithExternalProviderOutcome, ExternalLoginResponse>(value => value switch
        {
            SignInWithExternalProviderOutcome.Authenticated authenticated => new ExternalLoginResponse.Authenticated(
                new TokenResponse(
                    authenticated.AccessToken,
                    authenticated.AccessTokenExpiresAt,
                    authenticated.RefreshToken,
                    authenticated.RefreshTokenExpiresAt),
                authenticated.AccountCreated),
            SignInWithExternalProviderOutcome.TwoFactorRequired twoFactor =>
                new ExternalLoginResponse.TwoFactorRequired(twoFactor.ChallengeToken),

            // A branch added to the hierarchy without one here would otherwise be served as a
            // success with no body at all — and, on this endpoint, a second factor served as an
            // empty 200 is a second factor a client would skip.
            _ => throw new NotSupportedException(
                $"'{value.GetType().Name}' has no HTTP contract: add a branch to ExternalLoginResponse."),
        });

    public static Result<TokenResponse> ToTokenResponse(Result<RefreshAccessTokenOutcome> result) =>
        result.Map(value => new TokenResponse(
            value.AccessToken,
            value.AccessTokenExpiresAt,
            value.RefreshToken,
            value.RefreshTokenExpiresAt));

    public static Result<SetUpTwoFactorResponse> ToSetUpTwoFactorResponse(
        Result<SetUpTwoFactorOutcome> result) =>
        result.Map(value => new SetUpTwoFactorResponse(value.SharedKey, value.AuthenticatorUri));

    public static Result<ConfirmTwoFactorSetupResponse> ToConfirmTwoFactorSetupResponse(
        Result<ConfirmTwoFactorSetupOutcome> result) =>
        result.Map(value => new ConfirmTwoFactorSetupResponse(value.RecoveryCodes));

    public static Result<CurrentUserResponse> ToCurrentUserResponse(
        Result<GetCurrentUserOutcome> result) =>
        result.Map(value => new CurrentUserResponse(
            value.UserId,
            value.UserName,
            value.Email,
            value.EmailConfirmed,
            value.Roles,
            value.CreatedAt,
            value.TwoFactorEnabled));
}

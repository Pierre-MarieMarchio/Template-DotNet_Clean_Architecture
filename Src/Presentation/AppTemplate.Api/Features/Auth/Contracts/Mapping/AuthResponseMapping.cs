using System.Buffers.Text;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.ListSessions;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.ConfirmTwoFactorSetup;
using AppTemplate.Application.Auth.Features.TwoFactor.UseCases.Commands.SetUpTwoFactor;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Api.Features.Auth.Contracts.Mapping;

internal static class AuthResponseMapping
{
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

            _ => throw new NotSupportedException(
                $"'{value.GetType().Name}' has no HTTP contract: add a branch to LoginResponse."),
        });

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
            value.Email,
            value.EmailConfirmed,
            value.Roles,
            value.CreatedAt,
            value.TwoFactorEnabled));

    public static Result<SessionsResponse> ToSessionsResponse(
        Result<IReadOnlyList<ListSessionsItem>> result) =>
        result.Map(value => new SessionsResponse(
        [
            .. value.Select(session => new SessionResponse(
                session.Id,
                session.CreatedAt,
                session.LastUsedAt,
                session.UserAgent,
                session.Current)),
        ]));

    public static Result<PasskeysResponse> ToPasskeysResponse(
        Result<IReadOnlyList<PasskeyCredential>> result) =>
        result.Map(value => new PasskeysResponse(
        [
            .. value.Select(passkey => new PasskeyResponse(
                Base64Url.EncodeToString(passkey.CredentialId),
                passkey.Name,
                passkey.CreatedAt)),
        ]));

    public static Result<PasskeyResponse> ToPasskeyResponse(Result<PasskeyCredential> result) =>
        result.Map(passkey => new PasskeyResponse(
            Base64Url.EncodeToString(passkey.CredentialId),
            passkey.Name,
            passkey.CreatedAt));

    public static Result<PasskeyRegistrationOptionsResponse> ToRegistrationOptionsResponse(
        Result<PasskeyRegistrationChallenge> result) =>
        result.Map(challenge => new PasskeyRegistrationOptionsResponse(
            challenge.CreationOptions,
            challenge.RegistrationState));

    public static Result<PasskeySignInOptionsResponse> ToSignInOptionsResponse(
        Result<PasskeySignInChallenge> result) =>
        result.Map(challenge => new PasskeySignInOptionsResponse(
            challenge.SignInOptions,
            challenge.AuthenticationState));

    public static Result<PagedResponse<AccountSummaryResponse>> ToAccountPageResponse(
        Result<PagedResult<AccountSummary>> result) =>
        result.Map(value => PagedResponse.From(value, ToAccountSummaryResponse));

    public static Result<PagedResponse<SecurityEventResponse>> ToSecurityEventPageResponse(
        Result<PagedResult<SecurityEventEntry>> result) =>
        result.Map(value => PagedResponse.From(value, ToSecurityEventResponse));

    private static AccountSummaryResponse ToAccountSummaryResponse(AccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return new AccountSummaryResponse(
            account.Id,
            account.Email,
            account.EmailConfirmed,
            account.LockedOut,
            account.TwoFactorEnabled,
            account.Roles,
            account.CreatedAt);
    }

    private static SecurityEventResponse ToSecurityEventResponse(SecurityEventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new SecurityEventResponse(
            entry.Id,
            entry.Kind.ToString(),
            entry.OccurredAt,
            entry.IpAddress,
            entry.UserAgent,
            entry.Detail);
    }
}

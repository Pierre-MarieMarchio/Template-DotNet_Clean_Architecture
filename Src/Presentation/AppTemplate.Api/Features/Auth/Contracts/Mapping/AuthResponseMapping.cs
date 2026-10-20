using System.Buffers.Text;
using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
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

/// <summary>Projects use-case output onto the HTTP contract.</summary>
/// <remarks>Each lift tests <see cref="Result.IsFailure"/> on its own line before touching <c>Result{T}.Value</c>, which throws on a failure.</remarks>
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

    /// <summary>
    /// The passkey list. The credential id travels base64url-encoded — a WebAuthn id is opaque
    /// bytes, and this is the encoding the removal route takes it back in, so a client that holds
    /// the list can address an item without converting anything itself.
    /// </summary>
    public static Result<PasskeysResponse> ToPasskeysResponse(
        Result<IReadOnlyList<PasskeyCredential>> result) =>
        result.Map(value => new PasskeysResponse(
        [
            .. value.Select(passkey => new PasskeyResponse(
                Base64Url.EncodeToString(passkey.CredentialId),
                passkey.Name,
                passkey.CreatedAt)),
        ]));

    /// <summary>The registered passkey, as the registration's answer names it.</summary>
    public static Result<PasskeyResponse> ToPasskeyResponse(Result<PasskeyCredential> result) =>
        result.Map(passkey => new PasskeyResponse(
            Base64Url.EncodeToString(passkey.CredentialId),
            passkey.Name,
            passkey.CreatedAt));

    /// <summary>
    /// The registration options, with the ceremony's state exactly as the client must post it
    /// back: opaque, not a value this contract interprets.
    /// </summary>
    public static Result<PasskeyRegistrationOptionsResponse> ToRegistrationOptionsResponse(
        Result<PasskeyRegistrationChallenge> result) =>
        result.Map(challenge => new PasskeyRegistrationOptionsResponse(
            challenge.CreationOptions,
            challenge.RegistrationState));

    /// <summary>The sign-in options, with the ceremony's state exactly as the client must post it back.</summary>
    public static Result<PasskeySignInOptionsResponse> ToSignInOptionsResponse(
        Result<PasskeySignInChallenge> result) =>
        result.Map(challenge => new PasskeySignInOptionsResponse(
            challenge.SignInOptions,
            challenge.AuthenticationState));

    /// <summary>
    /// One page of the security trail. The item projection is named once and
    /// <see cref="PagedResponse.From{TSource,TItem}"/> carries the paging metadata, so the seven
    /// members are not restated per feature.
    /// </summary>
    public static Result<PagedResponse<SecurityEventResponse>> ToSecurityEventPageResponse(
        Result<PagedResult<SecurityEventEntry>> result) =>
        result.Map(value => PagedResponse.From(value, ToSecurityEventResponse));

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

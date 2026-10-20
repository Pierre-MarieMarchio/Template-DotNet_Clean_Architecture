using System.Buffers.Text;
using System.Reflection;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Mapping;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.ExternalLogins.UseCases.Commands.SignInWithExternalProvider;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.RefreshAccessToken;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Queries.GetCurrentUser;
using AppTemplate.Application.Core.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Contracts.Mapping;

public sealed class AuthResponseMappingTests
{
    [Fact]
    public void RegisterResponse_CarriesNothingButItsStatus()
    {
        typeof(RegisterResponse).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).ShouldBe(["Status"]);
        RegisterResponse.Pending.Status.ShouldBe("pending");
    }

    [Fact]
    public void ToLoginResponse_MapsAuthenticated_ToEveryTokenField()
    {
        var outcome = new LoginOutcome.Authenticated(
            UserId: Guid.NewGuid(),
            Email: "ada@example.com",
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry);

        var response = AuthResponseMapping.ToLoginResponse(Result.Success<LoginOutcome>(outcome)).Value;

        var authenticated = response.ShouldBeOfType<LoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldBe("access");
        authenticated.Tokens.AccessTokenExpiresAt.ShouldBe(_accessExpiry);
        authenticated.Tokens.RefreshToken.ShouldBe("refresh");
        authenticated.Tokens.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
    }

    [Fact]
    public void LoginResponse_Authenticated_PublishesNoProfileField()
    {
        string[] names = Array.ConvertAll(typeof(LoginResponse.Authenticated).GetProperties(), property => property.Name);

        names.ShouldBe(["Tokens"]);
    }

    [Fact]
    public void ToLoginResponse_MapsTwoFactorRequired()
    {
        var outcome = new LoginOutcome.TwoFactorRequired("challenge");

        var response = AuthResponseMapping.ToLoginResponse(Result.Success<LoginOutcome>(outcome)).Value;

        response.ShouldBeOfType<LoginResponse.TwoFactorRequired>().ChallengeToken.ShouldBe("challenge");
    }

    [Fact]
    public void ToLoginResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthResponseMapping.ToLoginResponse(Result.Failure<LoginOutcome>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToExternalLoginResponse_MapsAuthenticated_ToEveryTokenFieldAndTheCreationFlag()
    {
        var outcome = new SignInWithExternalProviderOutcome.Authenticated(
            UserId: Guid.NewGuid(),
            Email: "ada@example.com",
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry,
            AccountCreated: true);

        var response = AuthResponseMapping
            .ToExternalLoginResponse(Result.Success<SignInWithExternalProviderOutcome>(outcome)).Value;

        var authenticated = response.ShouldBeOfType<ExternalLoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldBe("access");
        authenticated.Tokens.AccessTokenExpiresAt.ShouldBe(_accessExpiry);
        authenticated.Tokens.RefreshToken.ShouldBe("refresh");
        authenticated.Tokens.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
        authenticated.AccountCreated.ShouldBeTrue();
    }

    [Fact]
    public void ToExternalLoginResponse_CarriesAccountCreated_ForASignInThatCreatedNothing()
    {
        var outcome = new SignInWithExternalProviderOutcome.Authenticated(
            UserId: Guid.NewGuid(),
            Email: "ada@example.com",
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry,
            AccountCreated: false);

        var response = AuthResponseMapping
            .ToExternalLoginResponse(Result.Success<SignInWithExternalProviderOutcome>(outcome)).Value;

        response.ShouldBeOfType<ExternalLoginResponse.Authenticated>().AccountCreated.ShouldBeFalse();
    }

    [Fact]
    public void ToExternalLoginResponse_MapsTwoFactorRequired()
    {
        var outcome = new SignInWithExternalProviderOutcome.TwoFactorRequired("challenge");

        var response = AuthResponseMapping
            .ToExternalLoginResponse(Result.Success<SignInWithExternalProviderOutcome>(outcome)).Value;

        response.ShouldBeOfType<ExternalLoginResponse.TwoFactorRequired>().ChallengeToken.ShouldBe("challenge");
    }

    [Fact]
    public void ExternalLoginResponse_Authenticated_PublishesNoProfileField()
    {
        string[] names = Array.ConvertAll(
            typeof(ExternalLoginResponse.Authenticated).GetProperties(),
            property => property.Name);

        names.ShouldBe(
            ["Tokens", "AccountCreated"],
            "the account this signed in as is read from GET /auth/me, exactly as it is after a "
            + "password sign-in — there is one definition of a profile.");
    }

    [Fact]
    public void ToExternalLoginResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthResponseMapping.ToExternalLoginResponse(
                Result.Failure<SignInWithExternalProviderOutcome>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToTokenResponse_CopiesEveryField()
    {
        var dto = new RefreshAccessTokenOutcome(
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry);

        var response = AuthResponseMapping.ToTokenResponse(Result.Success(dto)).Value;

        response.AccessToken.ShouldBe("access");
        response.AccessTokenExpiresAt.ShouldBe(_accessExpiry);
        response.RefreshToken.ShouldBe("refresh");
        response.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
    }

    [Fact]
    public void ToTokenResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthResponseMapping.ToTokenResponse(
                Result.Failure<RefreshAccessTokenOutcome>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToCurrentUserResponse_CopiesEveryField()
    {
        var userId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var dto = new GetCurrentUserOutcome(
            UserId: userId,
            Email: "ada@example.com",
            EmailConfirmed: true,
            Roles: ["Administrator", "Member"],
            CreatedAt: createdAt,
            TwoFactorEnabled: true);

        var response = AuthResponseMapping.ToCurrentUserResponse(Result.Success(dto)).Value;

        response.UserId.ShouldBe(userId);
        response.Email.ShouldBe("ada@example.com");
        response.EmailConfirmed.ShouldBeTrue();
        response.Roles.ShouldBe(["Administrator", "Member"]);
        response.CreatedAt.ShouldBe(createdAt);
        response.TwoFactorEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ToCurrentUserResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthResponseMapping.ToCurrentUserResponse(
                Result.Failure<GetCurrentUserOutcome>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void Authenticated_SerialisesWithTheStatusTag_AndNestsTheTokens()
    {
        LoginResponse response = new LoginResponse.Authenticated(
            new TokenResponse("access", _accessExpiry, "refresh", _refreshExpiry));

        string json = JsonSerializer.Serialize(response, _webJson);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("status").GetString().ShouldBe("authenticated");

        var tokens = root.GetProperty("tokens");
        tokens.GetProperty("accessToken").GetString().ShouldBe("access");
        tokens.GetProperty("accessTokenExpiresAt").GetDateTimeOffset().ShouldBe(_accessExpiry);
        tokens.GetProperty("refreshToken").GetString().ShouldBe("refresh");
        tokens.GetProperty("refreshTokenExpiresAt").GetDateTimeOffset().ShouldBe(_refreshExpiry);
    }

    [Fact]
    public void Authenticated_SerialisesNoProfileField()
    {
        LoginResponse response = new LoginResponse.Authenticated(
            new TokenResponse("access", _accessExpiry, "refresh", _refreshExpiry));

        string json = JsonSerializer.Serialize(response, _webJson);

        using var document = JsonDocument.Parse(json);
        string[] names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        names.ShouldBe(["status", "tokens"]);
    }

    [Fact]
    public void TwoFactorRequired_SerialisesWithItsOwnStatusTag()
    {
        LoginResponse response = new LoginResponse.TwoFactorRequired("challenge");

        string json = JsonSerializer.Serialize(response, _webJson);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("status").GetString().ShouldBe("twoFactorRequired");
        root.GetProperty("challengeToken").GetString().ShouldBe("challenge");
    }

    [Fact]
    public void ToSecurityEventPageResponse_CopiesEveryField_AndNamesTheKind()
    {
        var entry = new SecurityEventEntry(
            Guid.CreateVersion7(),
            SecurityEventKind.RefreshTokenReplayDetected,
            _accessExpiry,
            "203.0.113.7",
            "Mozilla/5.0",
            "invalidCredentials");

        var page = AuthResponseMapping.ToSecurityEventPageResponse(
            Result.Success(PagedResult.Offset([entry], 3, 20, 41))).Value;

        var item = page.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(entry.Id);
        item.Kind.ShouldBe(nameof(SecurityEventKind.RefreshTokenReplayDetected));
        item.OccurredAt.ShouldBe(_accessExpiry);
        item.IpAddress.ShouldBe("203.0.113.7");
        item.UserAgent.ShouldBe("Mozilla/5.0");
        item.Detail.ShouldBe("invalidCredentials");
        page.TotalCount.ShouldBe(41);
        page.Page.ShouldBe(3);
        page.PageSize.ShouldBe(20);
    }

    [Fact]
    public void ToSecurityEventPageResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthResponseMapping.ToSecurityEventPageResponse(
                Result.Failure<PagedResult<SecurityEventEntry>>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToPasskeysResponse_EncodesEveryCredentialIdAsBase64Url()
    {
        var credentialId = new byte[] { 1, 2, 3, 250, 251, 252, 253, 254, 255 };
        var listed = new[] { new PasskeyCredential(credentialId, "YubiKey 5", _accessExpiry) };

        var response = AuthResponseMapping.ToPasskeysResponse(
            Result.Success<IReadOnlyList<PasskeyCredential>>(listed)).Value;

        var item = response.Passkeys.ShouldHaveSingleItem();
        item.Id.ShouldBe(Base64Url.EncodeToString(credentialId));
        item.Name.ShouldBe("YubiKey 5");
        item.CreatedAt.ShouldBe(_accessExpiry);
    }

    [Fact]
    public void ToPasskeyResponse_EncodesTheCredentialIdAsBase64Url()
    {
        var credentialId = new byte[] { 9, 8, 7 };

        var response = AuthResponseMapping.ToPasskeyResponse(
            Result.Success(new PasskeyCredential(credentialId, null, _accessExpiry))).Value;

        response.Id.ShouldBe(Base64Url.EncodeToString(credentialId));
        response.Name.ShouldBeNull();
        response.CreatedAt.ShouldBe(_accessExpiry);
    }

    [Fact]
    public void ToRegistrationOptionsResponse_CopiesTheOptionsAndTheStateUnchanged()
    {
        var response = AuthResponseMapping.ToRegistrationOptionsResponse(
            Result.Success(new PasskeyRegistrationChallenge("the-creation-options", "the-state"))).Value;

        response.CreationOptions.ShouldBe("the-creation-options");
        response.RegistrationState.ShouldBe("the-state");
    }

    [Fact]
    public void ToSignInOptionsResponse_CopiesTheOptionsAndTheStateUnchanged()
    {
        var response = AuthResponseMapping.ToSignInOptionsResponse(
            Result.Success(new PasskeySignInChallenge("the-sign-in-options", "the-state"))).Value;

        response.SignInOptions.ShouldBe("the-sign-in-options");
        response.AuthenticationState.ShouldBe("the-state");
    }

    private static readonly JsonSerializerOptions _webJson = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset _accessExpiry = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset _refreshExpiry = new(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

    private static readonly Error _someRefusal = Error.Unauthorized("auth.invalidCredentials", "refused");
}

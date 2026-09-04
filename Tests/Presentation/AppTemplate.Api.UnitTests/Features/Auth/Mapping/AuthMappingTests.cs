using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.Features.Auth.Mapping;
using AppTemplate.Application.Common;
using Shouldly;
using Xunit;
using ApplicationCurrentUserResponse = AppTemplate.Application.Features.Auth.UseCases.Queries.GetCurrentUser.CurrentUserResponse;
using ApplicationLoginOutcome = AppTemplate.Application.Features.Auth.UseCases.Commands.Login.LoginOutcome;
using ApplicationRefreshAccessTokenResponse = AppTemplate.Application.Features.Auth.UseCases.Commands.RefreshAccessToken.RefreshAccessTokenResponse;
using ApplicationRegisterResponse = AppTemplate.Application.Features.Auth.UseCases.Commands.Register.RegisterResponse;

namespace AppTemplate.Api.UnitTests.Features.Auth.Mapping;

/// <summary>
/// A hand-written projection fails by omission: a field nobody copied still compiles and still
/// serialises, just empty. Every assertion here names every field for that reason.
/// </summary>
public sealed class AuthMappingTests
{
    [Fact]
    public void ToRegisterResponse_CopiesEveryField()
    {
        var dto = new ApplicationRegisterResponse(
            UserId: Guid.NewGuid(),
            UserName: "ada",
            Email: "ada@example.com",
            ConfirmationEmailSent: true);

        var response = AuthMapping.ToRegisterResponse(Result.Success(dto)).Value;

        response.UserName.ShouldBe("ada");
        response.Email.ShouldBe("ada@example.com");
        response.ConfirmationEmailSent.ShouldBeTrue();
    }

    /// <summary>
    /// A contract decision, not an oversight: the id is not needed to finish signing up, and
    /// publishing it hands out an internal identifier for nothing.
    /// </summary>
    [Fact]
    public void RegisterResponse_PublishesNoUserId()
    {
        typeof(RegisterResponse).GetProperty("UserId").ShouldBeNull();
    }

    [Fact]
    public void ToRegisterResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthMapping.ToRegisterResponse(Result.Failure<ApplicationRegisterResponse>(_someConflict)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someConflict);
    }

    [Fact]
    public void ToLoginResponse_MapsAuthenticated_ToEveryTokenField()
    {
        var outcome = new ApplicationLoginOutcome.Authenticated(
            UserId: Guid.NewGuid(),
            UserName: "ada",
            Email: "ada@example.com",
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry);

        var response = AuthMapping.ToLoginResponse(Result.Success<ApplicationLoginOutcome>(outcome)).Value;

        var authenticated = response.ShouldBeOfType<LoginResponse.Authenticated>();
        authenticated.Tokens.AccessToken.ShouldBe("access");
        authenticated.Tokens.AccessTokenExpiresAt.ShouldBe(_accessExpiry);
        authenticated.Tokens.RefreshToken.ShouldBe("refresh");
        authenticated.Tokens.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
    }

    /// <summary>
    /// Signing in answers with tokens and nothing else. The profile lives on <c>GET /auth/me</c>, so
    /// there is one definition of it rather than two that can drift.
    /// </summary>
    [Fact]
    public void LoginResponse_Authenticated_PublishesNoProfileField()
    {
        var names = Array.ConvertAll(typeof(LoginResponse.Authenticated).GetProperties(), property => property.Name);

        names.ShouldBe(["Tokens"]);
    }

    /// <summary>
    /// Nothing produces this branch yet. Mapping it anyway is the whole point of having declared it:
    /// the second factor ships without changing the shape clients already parse.
    /// </summary>
    [Fact]
    public void ToLoginResponse_MapsTwoFactorRequired()
    {
        var outcome = new ApplicationLoginOutcome.TwoFactorRequired("challenge");

        var response = AuthMapping.ToLoginResponse(Result.Success<ApplicationLoginOutcome>(outcome)).Value;

        response.ShouldBeOfType<LoginResponse.TwoFactorRequired>().ChallengeToken.ShouldBe("challenge");
    }

    [Fact]
    public void ToLoginResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthMapping.ToLoginResponse(Result.Failure<ApplicationLoginOutcome>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToTokenResponse_CopiesEveryField()
    {
        var dto = new ApplicationRefreshAccessTokenResponse(
            AccessToken: "access",
            AccessTokenExpiresAt: _accessExpiry,
            RefreshToken: "refresh",
            RefreshTokenExpiresAt: _refreshExpiry);

        var response = AuthMapping.ToTokenResponse(Result.Success(dto)).Value;

        response.AccessToken.ShouldBe("access");
        response.AccessTokenExpiresAt.ShouldBe(_accessExpiry);
        response.RefreshToken.ShouldBe("refresh");
        response.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
    }

    [Fact]
    public void ToTokenResponse_PropagatesAFailure_WithoutReadingTheValue()
    {
        var result = Should.NotThrow(
            () => AuthMapping.ToTokenResponse(
                Result.Failure<ApplicationRefreshAccessTokenResponse>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    [Fact]
    public void ToCurrentUserResponse_CopiesEveryField()
    {
        var userId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var dto = new ApplicationCurrentUserResponse(
            UserId: userId,
            UserName: "ada",
            Email: "ada@example.com",
            EmailConfirmed: true,
            Roles: ["Administrator", "Member"],
            CreatedAt: createdAt,
            TwoFactorEnabled: true);

        var response = AuthMapping.ToCurrentUserResponse(Result.Success(dto)).Value;

        response.UserId.ShouldBe(userId);
        response.UserName.ShouldBe("ada");
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
            () => AuthMapping.ToCurrentUserResponse(
                Result.Failure<ApplicationCurrentUserResponse>(_someRefusal)));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_someRefusal);
    }

    /// <summary>
    /// The wire format is the contract: the <c>status</c> tag and the nesting under <c>tokens</c> are
    /// what a client is written against, so they are asserted on the JSON rather than on the type.
    /// </summary>
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
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

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

    // Proves the contract's shape, not what MVC serves: `response` is declared as the polymorphic
    // base on purpose, which is what puts the discriminator in the JSON below. MVC's own choice of
    // starting type comes from ObjectResult.DeclaredType, which ApiControllerBase sets — matching
    // JsonSerializerOptions here says nothing about that. Only a test that goes through the MVC
    // pipeline (an integration test) proves the wire body actually looks like this.
    private static readonly JsonSerializerOptions _webJson = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset _accessExpiry = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset _refreshExpiry = new(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

    private static readonly Error _someConflict = Error.Conflict("auth.registrationConflict", "taken");
    private static readonly Error _someRefusal = Error.Unauthorized("auth.invalidCredentials", "refused");
}

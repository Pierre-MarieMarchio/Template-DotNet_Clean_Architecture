using AppTemplate.Api.Features.Auth.Composition;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Application.Auth.Features.Sessions.Model.Errors;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Auth.Composition;

public sealed class BrowserSessionTests
{
    private static readonly DateTimeOffset _refreshExpiry = new(2026, 1, 9, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void AProviderSignInInCookieMode_HoldsTheTokenInTheCookie_AndKeepsTheRestOfTheAnswer()
    {
        var httpContext = new DefaultHttpContext();

        var delivered = BrowserSession.Deliver(
            httpContext,
            BrowserSession.CookieMode,
            Result.Success<ExternalLoginResponse>(new ExternalLoginResponse.Authenticated(ATokenPair(), AccountCreated: true)));

        var authenticated = delivered.Value.ShouldBeOfType<ExternalLoginResponse.Authenticated>();
        authenticated.Tokens.RefreshToken.ShouldBeNull("the body serialises without the token once the cookie holds it.");
        authenticated.Tokens.AccessToken.ShouldBe("access");
        authenticated.Tokens.RefreshTokenExpiresAt.ShouldBe(_refreshExpiry);
        authenticated.AccountCreated.ShouldBeTrue();

        var cookie = TheCookieSetOn(httpContext);
        cookie.Value.ToString().ShouldBe("refresh");
        cookie.HttpOnly.ShouldBeTrue();
        cookie.Secure.ShouldBeTrue();
        cookie.SameSite.ShouldBe(Microsoft.Net.Http.Headers.SameSiteMode.Strict);
        cookie.Path.ToString().ShouldBe("/api/v1/auth");
        cookie.Expires.ShouldBe(_refreshExpiry);
    }

    [Fact]
    public void ASignInInBodyMode_IsLeftAsItWas_AndSetsNoCookie()
    {
        var httpContext = new DefaultHttpContext();
        var result = Result.Success<LoginResponse>(new LoginResponse.Authenticated(ATokenPair()));

        BrowserSession.Deliver(httpContext, mode: null, result).ShouldBeSameAs(result);

        httpContext.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Fact]
    public void AChallenge_SetsNoCookie_EvenInCookieMode()
    {
        var httpContext = new DefaultHttpContext();
        var result = Result.Success<ExternalLoginResponse>(new ExternalLoginResponse.TwoFactorRequired("the-challenge"));

        BrowserSession.Deliver(httpContext, BrowserSession.CookieMode, result).ShouldBeSameAs(result);

        httpContext.Response.Headers.SetCookie.ShouldBeEmpty();
    }

    [Fact]
    public void ARefusedRefresh_ClearsTheCookie_OnTheSamePathWithTheSameFlags()
    {
        var httpContext = new DefaultHttpContext();

        BrowserSession.Rotate(httpContext, Result.Failure<TokenResponse>(SessionsErrors.InvalidRefreshToken));

        var cookie = TheCookieSetOn(httpContext);
        cookie.Value.ToString().ShouldBeEmpty();
        cookie.Expires.ShouldBe(DateTimeOffset.UnixEpoch);
        cookie.Path.ToString().ShouldBe("/api/v1/auth");
        cookie.HttpOnly.ShouldBeTrue();
        cookie.Secure.ShouldBeTrue();
    }

    [Fact]
    public void ARefreshThatFailedForAnotherReason_KeepsTheCookie()
    {
        var httpContext = new DefaultHttpContext();

        BrowserSession.Rotate(
            httpContext,
            Result.Failure<TokenResponse>(Error.Validation("request.validationFailed", "Refused.")));

        httpContext.Response.Headers.SetCookie.ShouldBeEmpty(
            "only a refusal of the token itself says the cookie is worthless; anything else may pass.");
    }

    [Fact]
    public void ThePath_FollowsThePathBaseTheApiIsServedUnder()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.PathBase = "/gateway";

        BrowserSession.Clear(httpContext);

        TheCookieSetOn(httpContext).Path.ToString().ShouldBe("/gateway/api/v1/auth");
    }

    [Theory]
    [InlineData("another=cookie")]
    [InlineData("refresh_token=")]
    public void AnAbsentOrEmptyCookie_PresentsNothing(string header)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = header;

        BrowserSession.Presented(httpContext.Request).ShouldBeNull();
    }

    [Theory]
    [InlineData("cookie", true)]
    [InlineData("Cookie", false)]
    [InlineData(null, false)]
    public void OnlyTheExactWordCookie_AsksForCookieMode(string? mode, bool requested) =>
        BrowserSession.IsRequested(mode).ShouldBe(requested);

    private static TokenResponse ATokenPair() =>
        new("access", _refreshExpiry.AddDays(-7), "refresh", _refreshExpiry);

    private static SetCookieHeaderValue TheCookieSetOn(HttpContext httpContext) =>
        SetCookieHeaderValue.Parse(httpContext.Response.Headers.SetCookie.ToString());
}

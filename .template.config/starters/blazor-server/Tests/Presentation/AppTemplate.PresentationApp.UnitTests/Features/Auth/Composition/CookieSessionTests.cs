using System.Security.Claims;
using System.Text;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.PresentationApp.Features.Auth.Composition;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Features.Auth.Composition;

public sealed class CookieSessionTests
{
    private const string _issuer = "https://localhost/app";
    private const string _audience = "app";

    private static readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes("cookie-session-tests-signing-key-0123456789-abcdef"));
    private static readonly SymmetricSecurityKey _strangersKey = new(Encoding.UTF8.GetBytes("a-key-this-app-never-issued-with-0123456789-abcdef"));

    private static readonly Guid _userId = Guid.CreateVersion7();
    private static readonly Guid _sessionId = Guid.CreateVersion7();
    private static readonly DateTimeOffset _refreshExpiry = new(2099, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly ILogoutUseCase _logout = Substitute.For<ILogoutUseCase>();

    [Fact]
    public async Task AnAccessTokenThisAppIssued_SignsTheCookieInWithItsCallerSessionAndRoles()
    {
        var context = Context();

        (await SignIn().SignInAsync(context, Authenticated(_key))).ShouldBeTrue();

        await _authentication.Received(1).SignInAsync(
            context,
            CookieAuthenticationDefaults.AuthenticationScheme,
            Arg.Is<ClaimsPrincipal>(principal =>
                principal.FindFirstValue(ClaimTypes.NameIdentifier) == _userId.ToString()
                && principal.FindFirstValue("sid") == _sessionId.ToString()
                && principal.IsInRole("Administrator")),
            Arg.Is<AuthenticationProperties>(properties =>
                properties.GetTokenValue(CookieSession.RefreshTokenName) == "refresh-token"
                && properties.ExpiresUtc == _refreshExpiry
                && properties.IsPersistent));
    }

    [Fact]
    public async Task AnAccessTokenSignedByAnotherKey_SignsNobodyIn()
    {
        var context = Context();

        (await SignIn().SignInAsync(context, Authenticated(_strangersKey))).ShouldBeFalse();

        await _authentication.DidNotReceiveWithAnyArgs().SignInAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task SigningOut_EndsTheSessionTheCookieHolds_ThenClearsTheCookie()
    {
        var context = Context();
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = CookieSession.RefreshTokenName, Value = "refresh-token" }]);
        _authentication.AuthenticateAsync(context, CookieAuthenticationDefaults.AuthenticationScheme).Returns(
            AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(), properties, CookieAuthenticationDefaults.AuthenticationScheme)));
        _logout.ExecuteAsync(Arg.Any<LogoutCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());

        await SignIn().SignOutAsync(context);

        await _logout.Received(1).ExecuteAsync(new LogoutCommand("refresh-token"), Arg.Any<CancellationToken>());
        await _authentication.Received(1).SignOutAsync(context, CookieAuthenticationDefaults.AuthenticationScheme, Arg.Any<AuthenticationProperties?>());
    }

    private CookieSession SignIn()
    {
        var bearer = new JwtBearerOptions
        {
            TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = _issuer,
                ValidAudience = _audience,
                IssuerSigningKey = _key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            },
        };
        var monitor = Substitute.For<IOptionsMonitor<JwtBearerOptions>>();
        monitor.Get(JwtBearerDefaults.AuthenticationScheme).Returns(bearer);

        return new CookieSession(monitor, _logout);
    }

    private DefaultHttpContext Context() =>
        new()
        {
            RequestServices = new ServiceCollection().AddSingleton(_authentication).BuildServiceProvider(),
        };

    private static LoginOutcome.Authenticated Authenticated(SecurityKey key)
    {
        string token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = _userId.ToString(),
                ["sid"] = _sessionId.ToString(),
                ["role"] = "Administrator",
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });

        return new LoginOutcome.Authenticated(_userId, "someone@example.com", token, DateTimeOffset.UtcNow.AddMinutes(15), "refresh-token", _refreshExpiry);
    }
}

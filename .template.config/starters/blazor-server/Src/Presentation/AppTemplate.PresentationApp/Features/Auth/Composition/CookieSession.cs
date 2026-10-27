using System.Security.Claims;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Login;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.Logout;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationApp.Features.Auth.Composition;

public sealed class CookieSession(IOptionsMonitor<JwtBearerOptions> bearer, ILogoutUseCase logout)
{
    internal const string RefreshTokenName = "refresh_token";

    public async Task<bool> SignInAsync(HttpContext context, LoginOutcome.Authenticated session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        var options = bearer.Get(JwtBearerDefaults.AuthenticationScheme);
        var validated = await options.TokenHandlers[0].ValidateTokenAsync(session.AccessToken, options.TokenValidationParameters);

        if (!validated.IsValid)
        {
            return false;
        }

        var identity = new ClaimsIdentity(
            validated.ClaimsIdentity.Claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            validated.ClaimsIdentity.NameClaimType,
            validated.ClaimsIdentity.RoleClaimType);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = session.RefreshTokenExpiresAt,
        };
        properties.StoreTokens([new AuthenticationToken { Name = RefreshTokenName, Value = session.RefreshToken }]);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);

        return true;
    }

    public async Task SignOutAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? refreshToken = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, RefreshTokenName);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await logout.ExecuteAsync(new LogoutCommand(refreshToken), context.RequestAborted);
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}

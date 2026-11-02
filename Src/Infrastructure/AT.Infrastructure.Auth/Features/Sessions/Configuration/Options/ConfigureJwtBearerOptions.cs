using System.Security.Claims;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;

internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    internal const string UnauthorizedCode = "auth.required";

    internal const string ForbiddenCode = "auth.forbidden";

    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var settings = jwtOptions.Value;

        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.SaveToken = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,

            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,

            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = settings.CreateSigningKey(),

            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                ArgumentNullException.ThrowIfNull(context);

                context.HandleResponse();

                return WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status401Unauthorized,
                    "Unauthorized",
                    UnauthorizedCode,
                    "This endpoint requires a valid access token.");
            },

            OnForbidden = context =>
            {
                ArgumentNullException.ThrowIfNull(context);

                return WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    ForbiddenCode,
                    "The authenticated caller is not permitted to access this resource.");
            },

            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity || !identity.Claims.Any())
                {
                    context.Fail("This token carries no claims.");
                    return;
                }

                if (identity.FindFirst("AspNet.Identity.SecurityStamp") is null)
                {
                    context.Fail("This token carries no security stamp.");
                    return;
                }

                var signInManager = context.HttpContext.RequestServices
                    .GetRequiredService<SignInManager<AppUser>>();

                if (await signInManager.ValidateSecurityStampAsync(context.Principal) is null)
                {
                    context.Fail("This token's security stamp is no longer valid.");
                }
            },
        };
    }

    private static Task WriteProblemAsync(
        HttpContext httpContext,
        int status,
        string title,
        string code,
        string detail)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.io/{status}",
        };

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;

        return httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            httpContext.RequestAborted);
    }
}

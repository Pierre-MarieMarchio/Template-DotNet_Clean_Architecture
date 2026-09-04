using System.Security.Claims;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Bearer;

/// <summary>
/// Configures bearer validation from the validated <see cref="JwtOptions"/>. Done through
/// <see cref="IConfigureNamedOptions{TOptions}"/> rather than an inline lambda over a concrete
/// settings singleton, so the key is read after validation has run and a bad configuration cannot
/// reach the handler.
/// </summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    /// <summary>
    /// The stable code a client branches on when it has no usable token. Distinct from
    /// <see cref="ForbiddenCode"/>, because the two call for different behaviour: refresh and retry
    /// versus stop and tell the user.
    /// </summary>
    internal const string UnauthorizedCode = "auth.required";

    /// <summary>The stable code for an authenticated caller that is not permitted.</summary>
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

            // Never conditional. The previous code wrote `ValidateIssuer = !string.IsNullOrEmpty(...)`,
            // so leaving the issuer blank in configuration silently switched the check off instead of
            // failing; both values are now required by JwtOptionsValidator.
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,

            // Small, not zero. The issuer stamps nbf and exp from IDateTimeProvider while this check
            // reads the machine clock, so the two are the same instant only while that machine's
            // clock holds still. At zero tolerance a single backward step — an NTP correction, a
            // resumed VM — refuses every token already in circulation as "not yet valid", across
            // every instance behind the load balancer at once. Far below the framework's five-minute
            // default, which is loose enough to keep a stolen token alive well past its expiry.
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidIssuer = settings.Issuer,
            ValidAudience = settings.Audience,
            IssuerSigningKey = settings.CreateSigningKey(),

            // Pinned, so a token cannot dictate its own algorithm.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };

        options.Events = new JwtBearerEvents
        {
            // 401 and 403 are the two most common failures this API produces, and they used to be the
            // two that did not look like the others: a bare `{"message":"..."}` as application/json,
            // with no code, while every other failure was application/problem+json carrying a stable,
            // machine-readable `code`. A client therefore had to special-case the most frequent
            // response it would ever see, and had nothing but English prose to branch on.
            OnChallenge = context =>
            {
                ArgumentNullException.ThrowIfNull(context);

                // Suppresses the handler's own empty 401 so this response is the only one written.
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
                // The security stamp is what makes a password change or a forced sign-out take
                // effect before the access token expires.
                if (context.Principal?.Identity is not ClaimsIdentity identity || identity.Claims.Any() is false)
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

    /// <summary>
    /// Writes the same RFC 7807 shape the rest of the API writes: a <c>ProblemDetails</c> body, the
    /// <c>application/problem+json</c> content type, and a <c>code</c> extension a client can switch on.
    /// <para>
    /// The detail deliberately says nothing about <em>why</em> the token was rejected — expired, wrong
    /// signature, revoked security stamp, absent — because each of those answers tells an unauthenticated
    /// caller something about the system's state.
    /// </para>
    /// </summary>
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

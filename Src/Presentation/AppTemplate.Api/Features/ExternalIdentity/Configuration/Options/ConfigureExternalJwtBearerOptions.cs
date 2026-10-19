using System.Security.Claims;
using AppTemplate.Api.Features.ExternalIdentity.Composition;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.Features.ExternalIdentity.Configuration.Options;

/// <summary>
/// Configures bearer validation against the external provider from the validated
/// <see cref="AuthenticationExternalOptions"/>, read after validation has run so a bad configuration
/// cannot reach the handler.
/// </summary>
/// <remarks>
/// A refused token gets the handler's bare 401, which the status-code pages turn into the API's usual
/// problem body with the <c>auth.required</c> code; a refused policy gets <c>auth.forbidden</c> the same
/// way. Why a token was refused is never said to its bearer.
/// </remarks>
internal sealed class ConfigureExternalJwtBearerOptions(
    IOptions<AuthenticationExternalOptions> externalOptions,
    IHttpClientFactory httpClientFactory) : IConfigureNamedOptions<JwtBearerOptions>
{
    /// <summary>
    /// The named client the discovery document and the signing keys are fetched with, so the fetch
    /// runs under the outbound defaults every host installs rather than on a client of its own.
    /// </summary>
    internal const string BackchannelClientName = "ExternalIdentityProvider";

    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var settings = externalOptions.Value;

        options.Authority = settings.Authority;
        options.Audience = settings.Audience;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.SaveToken = false;
        options.Backchannel = httpClientFactory.CreateClient(BackchannelClientName);

        // Claim types stay as the provider wrote them. With the framework's mapping on, `sub` would
        // arrive as NameIdentifier, and a provider whose subjects are GUIDs would hand its subject
        // straight to CurrentUser as an owner id.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // The issuer and the keys come from the authority's discovery document.
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,

            // Small, not zero, for the same reason as the local tokens: two clocks are involved.
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = settings.NameClaim,
            RoleClaimType = ClaimTypes.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var mapper = context.HttpContext.RequestServices.GetRequiredService<ExternalPrincipalMapper>();

                var principal = context.Principal is null
                    ? null
                    : await mapper.MapAsync(context.Principal, context.SecurityToken, context.HttpContext.RequestAborted);

                if (principal is null)
                {
                    context.Fail("The token names no subject this API can map to an owner.");
                    return;
                }

                context.Principal = principal;
            },
        };
    }
}

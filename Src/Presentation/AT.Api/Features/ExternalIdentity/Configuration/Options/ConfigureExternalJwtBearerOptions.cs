using System.Security.Claims;
using AppTemplate.Api.Features.ExternalIdentity.Composition;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Api.Features.ExternalIdentity.Configuration.Options;

internal sealed class ConfigureExternalJwtBearerOptions(
    IOptions<AuthenticationExternalOptions> externalOptions,
    IHttpClientFactory httpClientFactory) : IConfigureNamedOptions<JwtBearerOptions>
{
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

        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,

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

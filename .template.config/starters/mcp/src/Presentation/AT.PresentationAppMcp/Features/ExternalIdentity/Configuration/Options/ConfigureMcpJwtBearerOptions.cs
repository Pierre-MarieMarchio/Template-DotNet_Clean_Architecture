using System.Security.Claims;
using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationAppMcp.Common.Hosting;
using AppTemplate.PresentationAppMcp.Features.ExternalIdentity.Composition;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.PresentationAppMcp.Features.ExternalIdentity.Configuration.Options;

internal sealed class ConfigureMcpJwtBearerOptions(
    IOptions<AuthenticationExternalOptions> externalOptions,
    IOptions<McpOptions> mcpOptions,
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
        string resource = mcpOptions.Value.Resource;

        options.Authority = settings.Authority;
        options.Audience = resource;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.SaveToken = false;
        options.Backchannel = httpClientFactory.CreateClient(BackchannelClientName);

        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudience = resource,
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
                    context.Fail("The token names no subject this server can map to an owner.");
                    return;
                }

                context.Principal = principal;
            },
        };
    }
}

using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AppTemplate.PresentationApp.Features.ExternalIdentity.Configuration.Options;

internal sealed class ConfigureExternalOpenIdConnectOptions(
    IOptions<AuthenticationExternalOptions> provider,
    IOptions<AuthenticationExternalInteractiveOptions> client) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    public void Configure(OpenIdConnectOptions options) =>
        Configure(OpenIdConnectDefaults.AuthenticationScheme, options);

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(name, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        options.Authority = provider.Value.Authority;
        options.RequireHttpsMetadata = provider.Value.RequireHttpsMetadata;
        options.ClientId = client.Value.ClientId;
        options.ClientSecret = client.Value.ClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;
        options.MapInboundClaims = false;
        options.Scope.Add("email");

        options.Events.OnTokenValidated = async context =>
        {
            var mapping = context.HttpContext.RequestServices.GetRequiredService<ExternalSignInMapping>();

            var principal = context.Principal is null
                ? null
                : await mapping.MapAsync(context.Principal, context.SecurityToken, context.HttpContext.RequestAborted);

            if (principal is null)
            {
                context.Fail("The provider's token names no subject this app can map to an owner.");
                return;
            }

            context.Principal = principal;
        };
    }
}

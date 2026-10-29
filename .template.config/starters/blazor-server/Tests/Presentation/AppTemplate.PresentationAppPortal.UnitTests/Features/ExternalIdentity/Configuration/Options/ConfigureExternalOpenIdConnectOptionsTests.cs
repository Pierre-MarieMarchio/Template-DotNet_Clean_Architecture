using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationApp.Features.ExternalIdentity.Configuration.Options;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationApp.UnitTests.Features.ExternalIdentity.Configuration.Options;

public sealed class ConfigureExternalOpenIdConnectOptionsTests
{
    [Fact]
    public void TheSignIn_IsTheCodeFlowWithPkce_AsTheConfiguredClientOfTheApisProvider()
    {
        var options = new OpenIdConnectOptions();

        Configure().Configure(OpenIdConnectDefaults.AuthenticationScheme, options);

        options.Authority.ShouldBe("https://idp.example.com/realms/app");
        options.ClientId.ShouldBe("app-portal");
        options.ResponseType.ShouldBe(OpenIdConnectResponseType.Code);
        options.UsePkce.ShouldBeTrue();
        options.SaveTokens.ShouldBeFalse("The cookie keeps the mapped principal, never the provider's tokens.");
        options.MapInboundClaims.ShouldBeFalse();
    }

    [Fact]
    public void AnotherSchemesOptions_AreLeftAlone()
    {
        var options = new OpenIdConnectOptions();

        Configure().Configure("another", options);

        options.ClientId.ShouldBeNull();
    }

    private static ConfigureExternalOpenIdConnectOptions Configure() =>
        new(
            Microsoft.Extensions.Options.Options.Create(new AuthenticationExternalOptions { Authority = "https://idp.example.com/realms/app", Audience = "app-api" }),
            Microsoft.Extensions.Options.Options.Create(new AuthenticationExternalInteractiveOptions { ClientId = "app-portal" }));
}

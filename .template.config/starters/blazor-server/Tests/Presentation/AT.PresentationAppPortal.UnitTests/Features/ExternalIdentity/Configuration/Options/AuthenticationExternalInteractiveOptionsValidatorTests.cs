using AppTemplate.PresentationAppPortal.Features.ExternalIdentity.Configuration.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Features.ExternalIdentity.Configuration.Options;

public sealed class AuthenticationExternalInteractiveOptionsValidatorTests
{
    [Fact]
    public void NoClientId_IsRefused() =>
        new AuthenticationExternalInteractiveOptionsValidator()
            .Validate(null, new AuthenticationExternalInteractiveOptions { ClientId = " " })
            .Failed.ShouldBeTrue();

    [Fact]
    public void AClientId_IsAccepted() =>
        new AuthenticationExternalInteractiveOptionsValidator()
            .Validate(null, new AuthenticationExternalInteractiveOptions { ClientId = "app-portal" })
            .Succeeded.ShouldBeTrue();
}

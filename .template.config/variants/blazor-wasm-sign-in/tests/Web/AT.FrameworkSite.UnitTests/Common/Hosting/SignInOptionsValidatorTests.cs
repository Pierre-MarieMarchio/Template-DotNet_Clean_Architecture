using AppTemplate.FrameworkSite.Common.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.FrameworkSite.UnitTests.Common.Hosting;

public sealed class SignInOptionsValidatorTests
{
    [Theory]
    [InlineData("https://login.example.com/")]
    [InlineData("https://login.example.com/realms/acme")]
    public void AnHttpsAuthority_AndAClient_AreValid(string authority) =>
        Validate(options => options.Authority = new Uri(authority)).Succeeded.ShouldBeTrue();

    [Fact]
    public void NoAuthority_IsRefused() =>
        Validate(options => options.Authority = null).FailureMessage.ShouldNotBeNull().ShouldContain("'SignIn:Authority'");

    [Theory]
    [InlineData("/realms/acme")]
    [InlineData("ftp://login.example.com/")]
    public void ARelativeOrNonHttpAuthority_IsRefused(string authority) =>
        Validate(options => options.Authority = new Uri(authority, UriKind.RelativeOrAbsolute)).FailureMessage.ShouldNotBeNull().ShouldContain("'SignIn:Authority'");

    [Fact]
    public void AnHttpAuthority_IsRefused_UnlessHttpsMetadataIsNotRequired()
    {
        var local = new Uri("http://localhost:8180/realms/acme");

        Validate(options => options.Authority = local).FailureMessage.ShouldNotBeNull().ShouldContain("'SignIn:RequireHttpsMetadata'");
        Validate(options =>
        {
            options.Authority = local;
            options.RequireHttpsMetadata = false;
        }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void NoClientId_IsRefused(string clientId) =>
        Validate(options => options.ClientId = clientId).FailureMessage.ShouldNotBeNull().ShouldContain("'SignIn:ClientId'");

    [Theory]
    [InlineData("profile email")]
    [InlineData("openidprofile")]
    public void AScopeWithoutOpenId_IsRefused(string scope) =>
        Validate(options => options.Scope = scope).FailureMessage.ShouldNotBeNull().ShouldContain("'SignIn:Scope'");

    [Fact]
    public void TheDefaultScope_AsksForAnIdentity_AndNoRefreshToken() =>
        new SignInOptions().Scope.ShouldBe("openid profile email");

    private static ValidateOptionsResult Validate(Action<SignInOptions> change)
    {
        var options = new SignInOptions { Authority = new Uri("https://login.example.com/"), ClientId = "browser-app" };
        change(options);

        return new SignInOptionsValidator().Validate(null, options);
    }
}

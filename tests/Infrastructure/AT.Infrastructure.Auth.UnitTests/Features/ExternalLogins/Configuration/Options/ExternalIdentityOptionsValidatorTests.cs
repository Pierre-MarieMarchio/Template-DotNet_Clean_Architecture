using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.ExternalLogins.Configuration.Options;

public sealed class ExternalIdentityOptionsValidatorTests
{
    private static readonly ExternalIdentityOptionsValidator _validator = new();

    [Fact]
    public void Validate_AcceptsADeploymentWithNoProviderAtAll() =>
        _validator.Validate(name: null, new ExternalIdentityOptions()).Succeeded.ShouldBeTrue();

    [Fact]
    public void Validate_AcceptsAFullyConfiguredProvider() =>
        _validator.Validate(name: null, With(Google())).Succeeded.ShouldBeTrue();

    [Fact]
    public void Validate_AcceptsAProviderAddressedByItsDiscoveryDocument()
    {
        var provider = Google();
        provider.JwksUri = string.Empty;
        provider.MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

        _validator.Validate(name: null, With(provider)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_RejectsAProviderWithNoName()
    {
        var provider = Google();
        provider.Name = "  ";

        FailureOf(With(provider)).ShouldContain("Name");
    }

    [Fact]
    public void Validate_RejectsTwoProvidersSharingAName()
    {
        var second = Google();
        second.Name = "GOOGLE";

        FailureOf(With(Google(), second)).ShouldContain("repeats");
    }

    [Fact]
    public void Validate_RejectsAProviderWithNoIssuer()
    {
        var provider = Google();
        provider.Issuers.Clear();

        FailureOf(With(provider)).ShouldContain("Issuers");
    }

    [Fact]
    public void Validate_RejectsAProviderWithNoAudience()
    {
        var provider = Google();
        provider.Audiences.Clear();

        FailureOf(With(provider)).ShouldContain("Audiences");
    }

    [Fact]
    public void Validate_RejectsABlankEntryInsideAPopulatedList()
    {
        var provider = Google();
        provider.Audiences.Add("   ");

        FailureOf(With(provider)).ShouldContain("Audiences");
    }

    [Fact]
    public void Validate_RejectsAProviderWithNoKeySetAddress()
    {
        var provider = Google();
        provider.JwksUri = string.Empty;

        FailureOf(With(provider)).ShouldContain("JwksUri");
    }

    [Fact]
    public void Validate_RejectsAProviderWithBothKeySetAddresses()
    {
        var provider = Google();
        provider.MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

        FailureOf(With(provider)).ShouldContain("not both");
    }

    [Theory]
    [InlineData("http://www.googleapis.com/oauth2/v3/certs")]
    [InlineData("/oauth2/v3/certs")]
    [InlineData("not a url at all")]
    public void Validate_RejectsAKeySetAddressThatIsNotAnAbsoluteHttpsUrl(string address)
    {
        var provider = Google();
        provider.JwksUri = address;

        FailureOf(With(provider)).ShouldContain("absolute https");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25 * 60)]
    public void Validate_RejectsAKeySetLifetimeOutsideItsBounds(int minutes)
    {
        var options = With(Google());
        options.KeySetLifetime = TimeSpan.FromMinutes(minutes);

        FailureOf(options).ShouldContain("KeySetLifetime");
    }

    private static string FailureOf(ExternalIdentityOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();

        return result.FailureMessage.ShouldNotBeNull();
    }

    private static ExternalIdentityProviderOptions Google()
    {
        var provider = new ExternalIdentityProviderOptions
        {
            Name = "google",
            JwksUri = "https://www.googleapis.com/oauth2/v3/certs",
        };

        provider.Issuers.Add("https://accounts.google.com");
        provider.Issuers.Add("accounts.google.com");
        provider.Audiences.Add("1234.apps.googleusercontent.com");

        return provider;
    }

    private static ExternalIdentityOptions With(params ExternalIdentityProviderOptions[] providers)
    {
        var options = new ExternalIdentityOptions();

        foreach (var provider in providers)
        {
            options.Providers.Add(provider);
        }

        return options;
    }
}

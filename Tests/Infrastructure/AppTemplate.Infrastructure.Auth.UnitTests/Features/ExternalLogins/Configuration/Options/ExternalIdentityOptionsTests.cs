using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.ExternalLogins.Configuration.Options;

public sealed class ExternalIdentityOptionsTests
{
    [Fact]
    public void TheSection_BindsEveryProviderAndEveryValueInIt()
    {
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ExternalIdentity:KeySetLifetime"] = "00:15:00",

            ["ExternalIdentity:Providers:0:Name"] = "google",
            ["ExternalIdentity:Providers:0:Issuers:0"] = "https://accounts.google.com",
            ["ExternalIdentity:Providers:0:Issuers:1"] = "accounts.google.com",
            ["ExternalIdentity:Providers:0:Audiences:0"] = "1234.apps.googleusercontent.com",
            ["ExternalIdentity:Providers:0:Audiences:1"] = "5678.apps.googleusercontent.com",
            ["ExternalIdentity:Providers:0:JwksUri"] = "https://www.googleapis.com/oauth2/v3/certs",

            ["ExternalIdentity:Providers:1:Name"] = "apple",
            ["ExternalIdentity:Providers:1:Issuers:0"] = "https://appleid.apple.com",
            ["ExternalIdentity:Providers:1:Audiences:0"] = "com.example.app",
            ["ExternalIdentity:Providers:1:MetadataAddress"] =
                "https://appleid.apple.com/.well-known/openid-configuration",
        });

        options.KeySetLifetime.ShouldBe(TimeSpan.FromMinutes(15));
        options.Providers.Count.ShouldBe(2);

        var google = options.Providers[0];
        google.Name.ShouldBe("google");
        google.Issuers.ShouldBe(["https://accounts.google.com", "accounts.google.com"]);
        google.Audiences.Count.ShouldBe(2);
        google.JwksUri.ShouldBe("https://www.googleapis.com/oauth2/v3/certs");
        google.MetadataAddress.ShouldBeEmpty();

        var apple = options.Providers[1];
        apple.Name.ShouldBe("apple");
        apple.MetadataAddress.ShouldBe("https://appleid.apple.com/.well-known/openid-configuration");
        apple.JwksUri.ShouldBeEmpty();
    }

    [Fact]
    public void TheSection_LeavesTheDefaultsInPlaceWhenItIsAbsentEntirely()
    {
        var options = Bind([]);

        options.Providers.ShouldBeEmpty();
        options.KeySetLifetime.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [Theory]
    [InlineData("google")]
    [InlineData("Google")]
    [InlineData("GOOGLE")]
    public void Find_MatchesTheProviderNameWhateverCaseTheClientUsed(string presented)
    {
        var options = new ExternalIdentityOptions();
        options.Providers.Add(new ExternalIdentityProviderOptions { Name = "google" });

        options.Find(presented).ShouldNotBeNull().Name.ShouldBe("google");
    }

    [Fact]
    public void Find_ReturnsNothingForAProviderNobodyConfigured() =>
        new ExternalIdentityOptions().Find("google").ShouldBeNull();

    private static ExternalIdentityOptions Bind(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var options = new ExternalIdentityOptions();

        configuration.GetSection(ExternalIdentityOptions.SectionName).Bind(options);

        return options;
    }
}

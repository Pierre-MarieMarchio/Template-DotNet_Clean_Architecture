using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Options;

/// <summary>
/// The validator runs under <c>ValidateOnStart</c>, so what it rejects is what stops the process
/// from booting — and, just as importantly here, what it accepts is what lets a deployment that does
/// not use external sign-in start at all.
/// </summary>
public sealed class ExternalIdentityOptionsValidatorTests
{
    private static readonly ExternalIdentityOptionsValidator _validator = new();

    /// <summary>
    /// The case that matters most to every project that never turns this feature on. An optional
    /// feature whose absent configuration stops the host is a regression for everyone who does not
    /// use it.
    /// </summary>
    [Fact]
    public void Validate_AcceptsADeploymentWithNoProviderAtAll()
    {
        _validator.Validate(name: null, new ExternalIdentityOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AcceptsAFullyConfiguredProvider()
    {
        _validator.Validate(name: null, With(Google())).Succeeded.ShouldBeTrue();
    }

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

    /// <summary>
    /// Two entries under one name means one of them is unreachable, and which one is an accident of
    /// ordering rather than a decision.
    /// </summary>
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

    /// <summary>
    /// A blank entry inside an otherwise populated list is worse than an empty list: it reads as
    /// configured and matches a token whose claim is also blank.
    /// </summary>
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

    /// <summary>
    /// The key set is the only thing standing between a forged token and an account, so fetching it
    /// over plaintext would put that decision in the hands of whoever is between the two hosts.
    /// </summary>
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

    /// <summary>
    /// The message a failure carries, asserted to exist before it is read: a validator that failed
    /// with nothing to say would satisfy every assertion below by accident.
    /// </summary>
    private static string FailureOf(ExternalIdentityOptions options)
    {
        var result = _validator.Validate(name: null, options);

        result.Failed.ShouldBeTrue();

        return result.FailureMessage.ShouldNotBeNull();
    }

    /// <summary>
    /// Google mints both forms of its issuer, so an installation has to be able to accept both — and
    /// several client identifiers, because one product routinely registers a web, an iOS and an
    /// Android client against the same API.
    /// </summary>
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

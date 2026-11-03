using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Security;

public sealed class SecurityHeaderOptionsTests
{
    private const string _portalPolicy = "default-src 'self'; frame-ancestors 'none'";

    private readonly SecurityHeaderOptionsValidator _validator = new();

    [Fact]
    public void TheDefaultPolicy_ForbidsEverythingAndFraming_AndIsValid()
    {
        SecurityHeaderOptions.DefaultContentSecurityPolicy
            .ShouldBe("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");

        _validator.Validate(name: null, new SecurityHeaderOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Fails_WhenThePolicyIsBlank(string policy)
    {
        var result = _validator.Validate(name: null, new SecurityHeaderOptions { ContentSecurityPolicy = policy });

        result.Failed.ShouldBeTrue();
        result.Failures!.ShouldHaveSingleItem().ShouldContain("must not be blank");
    }

    [Fact]
    public void Validate_Fails_WhenThePolicyHasAControlCharacter()
    {
        var options = new SecurityHeaderOptions { ContentSecurityPolicy = "default-src 'none'\r\nX: y; frame-ancestors 'none'" };

        _validator.Validate(name: null, options).Failures!.ShouldHaveSingleItem().ShouldContain("control character");
    }

    [Fact]
    public void Validate_Fails_WhenThePolicyForgetsFrameAncestors()
    {
        var options = new SecurityHeaderOptions { ContentSecurityPolicy = "default-src 'none'" };

        _validator.Validate(name: null, options).Failures!.ShouldHaveSingleItem().ShouldContain("frame-ancestors");
    }

    [Fact]
    public void TheFixedHeaders_AreTheThreeEveryHostSends()
    {
        SecurityHeaderValues.Fixed.ShouldBe(
            new Dictionary<string, string>
            {
                ["X-Content-Type-Options"] = "nosniff",
                ["X-Frame-Options"] = "DENY",
                ["Referrer-Policy"] = "no-referrer",
            },
            ignoreOrder: true);
    }

    [Fact]
    public void AddSecurityHeaderOptions_UsesTheDefaultTheHostGave_WhenNothingIsConfigured()
    {
        using ServiceProvider provider = Compose([], _portalPolicy);

        provider.GetRequiredService<IOptions<SecurityHeaderOptions>>().Value.ContentSecurityPolicy
            .ShouldBe(_portalPolicy);
    }

    [Fact]
    public void AddSecurityHeaderOptions_LetsTheConfigurationOverrideTheHostsDefault()
    {
        const string configured = "default-src 'none'; frame-ancestors 'self'";

        using ServiceProvider provider = Compose(
            new Dictionary<string, string?> { ["SecurityHeaders:ContentSecurityPolicy"] = configured },
            _portalPolicy);

        provider.GetRequiredService<IOptions<SecurityHeaderOptions>>().Value.ContentSecurityPolicy
            .ShouldBe(configured);
    }

    [Fact]
    public void AddSecurityHeaderOptions_RefusesAnInvalidPolicy_AtStartup()
    {
        using ServiceProvider provider = Compose(
            new Dictionary<string, string?> { ["SecurityHeaders:ContentSecurityPolicy"] = "default-src 'none'" },
            SecurityHeaderOptions.DefaultContentSecurityPolicy);

        Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<SecurityHeaderOptions>>().Value);
    }

    private static ServiceProvider Compose(Dictionary<string, string?> settings, string defaultPolicy)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();

        services.AddSecurityHeaderOptions(configuration, defaultPolicy);

        return services.BuildServiceProvider();
    }
}

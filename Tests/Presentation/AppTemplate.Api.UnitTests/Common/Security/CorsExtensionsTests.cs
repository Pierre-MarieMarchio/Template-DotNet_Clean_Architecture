using AppTemplate.Api.Common.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using CorsPolicy = Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy;
using FrameworkCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class CorsExtensionsTests
{
    private static readonly (string Header, string Written)[] _actionable =
    [
        ("Retry-After", "the rate limiter, on a 429"),
        ("ETag", "ApiControllerBase, and If-Match is how every conditional write is made"),
        ("Location", "ApiControllerBase, on a 201"),
        ("Idempotency-Replayed", "IdempotencyFilter, telling a stored answer from a fresh one"),
    ];

    [Fact]
    public void TheDefaultPolicy_ExposesEveryHeaderAClientHasToRead()
    {
        var policy = ResolvePolicy(["https://app.test"]);

        _actionable.Length.ShouldBeGreaterThanOrEqualTo(
            4,
            "this list is the point of the test; an empty or truncated one would assert nothing.");

        foreach ((string header, string written) in _actionable)
        {
            policy.ExposedHeaders.ShouldContain(
                header,
                $"'{header}' is written by {written}. Not naming it here does not fail anything on "
                + "the server: the header is sent, and the browser drops it before script sees it.");
        }
    }

    [Fact]
    public void TheDefaultPolicy_DoesNotAllowCredentials()
    {
        ResolvePolicy(["https://app.test"]).SupportsCredentials.ShouldBeFalse(
            "tokens travel in the Authorization header rather than a cookie, so credentials are "
            + "not needed and allowing them would widen the policy for nothing.");
    }

    [Fact]
    public void WithNoConfiguredOrigins_ThePolicyAllowsNone()
    {
        ResolvePolicy([]).Origins.ShouldBeEmpty(
            "an empty configuration must close the policy, not open it.");
    }

    [Fact]
    public void AnOriginThatIsNotAnOrigin_IsRefusedWhereTheOptionsAreRead()
    {
        var exception = Should.Throw<OptionsValidationException>(() => ResolvePolicy(["https://app.test/"]));

        exception.Message.ShouldContain("Cors:AllowedOrigins");
    }

    private static CorsPolicy ResolvePolicy(string[] origins)
    {
        var settings = origins
            .Select((origin, index) =>
                new KeyValuePair<string, string?>($"{CorsOptions.SectionName}:{nameof(CorsOptions.AllowedOrigins)}:{index}", origin));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddApiCors(configuration);

        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<FrameworkCorsOptions>>().Value
            .GetPolicy(CorsExtensions.Default);

        return policy.ShouldNotBeNull(
            $"'{CorsExtensions.Default}' is the policy name Program.cs applies; if it is not "
            + "registered under that name, nothing this test asserts reaches a request.");
    }
}

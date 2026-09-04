using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

/// <summary>
/// A browser hands script only the CORS-safelisted response headers unless the server names the
/// rest, so a header this API writes and does not expose reads as absent to a cross-origin client
/// — with no error anywhere to say why.
/// </summary>
public sealed class CorsExtensionsTests
{
    /// <summary>
    /// The headers this API writes that a client is expected to act on. Each one is asserted
    /// against where it is written, so that adding a header without exposing it fails here rather
    /// than in whichever browser reaches it first.
    /// </summary>
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

    /// <summary>
    /// The other half, and the reason this is a policy rather than a wildcard: credentials stay off,
    /// because tokens travel in the Authorization header and it is that pairing with a permissive
    /// origin list that turns a policy into a hole.
    /// </summary>
    [Fact]
    public void TheDefaultPolicy_DoesNotAllowCredentials()
    {
        ResolvePolicy(["https://app.test"]).SupportsCredentials.ShouldBeFalse(
            "tokens travel in the Authorization header rather than a cookie, so credentials are "
            + "not needed and allowing them would widen the policy for nothing.");
    }

    /// <summary>
    /// Nothing configured means allow nothing rather than allow everything. Same-origin callers are
    /// unaffected either way, because CORS governs cross-origin requests alone.
    /// </summary>
    [Fact]
    public void WithNoConfiguredOrigins_ThePolicyAllowsNone()
    {
        ResolvePolicy([]).Origins.ShouldBeEmpty(
            "an empty configuration must close the policy, not open it.");
    }

    private static CorsPolicy ResolvePolicy(string[] origins)
    {
        var settings = origins
            .Select((origin, index) =>
                new KeyValuePair<string, string?>($"{CorsExtensions.AllowedOriginsKey}:{index}", origin));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddApiCors(configuration);

        using var provider = services.BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value
            .GetPolicy(CorsExtensions.Default);

        return policy.ShouldNotBeNull(
            $"'{CorsExtensions.Default}' is the policy name Program.cs applies; if it is not "
            + "registered under that name, nothing this test asserts reaches a request.");
    }
}

using System.Net;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppPortal.UnitTests.Common.Security;

public sealed class SecurityHeaderTests
{
    [Fact]
    public async Task EveryResponse_CarriesTheFixedSecurityHeaders()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Referrer-Policy").ShouldBe("no-referrer");
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public async Task ThePolicy_ForbidsFraming_AndAnyScriptButTheHostsOwn()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        var policy = Directives(Header(response, "Content-Security-Policy"));

        policy["frame-ancestors"].ShouldBe("'none'");
        policy["script-src"].ShouldBe("'self'");
        policy["default-src"].ShouldBe("'self'");
        policy["object-src"].ShouldBe("'none'");
    }

    [Fact]
    public async Task ThePolicy_LetsTheCircuitReachItsOwnOrigin()
    {
        using var host = new PresentationHost();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Directives(Header(response, "Content-Security-Policy"))["connect-src"].ShouldBe("'self'");
    }

    private static Dictionary<string, string> Directives(string policy) =>
        policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(directive => directive.Split(' ', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);

    internal static string Header(HttpResponseMessage response, string name)
    {
        response.Headers.TryGetValues(name, out var values)
            .ShouldBeTrue($"No '{name}' header. Present: {string.Join(", ", response.Headers.Select(header => header.Key))}");

        return values!.Single();
    }
}

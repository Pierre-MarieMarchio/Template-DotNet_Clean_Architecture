using Shouldly;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The response-security headers, asserted from one place so that every test which cares about a new
/// kind of response can hold it to the same set.
/// </summary>
public static class SecurityHeaderAssertions
{
    public static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "Referrer-Policy").ShouldBe("no-referrer");
        Header(response, "X-Frame-Options").ShouldBe("DENY");

        string policy = Policy(response);
        policy.ShouldContain("default-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldContain("base-uri 'none'");

        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    public static string Policy(HttpResponseMessage response) =>
        Header(response, "Content-Security-Policy");

    public static string Header(HttpResponseMessage response, string name)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers.TryGetValues(name, out var values)
            .ShouldBeTrue(
                $"No '{name}' header. Present: {string.Join(", ", response.Headers.Select(header => header.Key))}");

        return values!.Single();
    }
}

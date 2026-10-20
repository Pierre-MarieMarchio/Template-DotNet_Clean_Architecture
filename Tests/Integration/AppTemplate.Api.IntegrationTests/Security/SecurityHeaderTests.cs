using System.Net;
//#if (signedInRoute)
using AppTemplate.Api.Common.Security;
//#endif
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class SecurityHeaderTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    //#if (signedInRoute)
    [Fact]
    public async Task ASuccessfulResponse_CarriesEverySecurityHeader()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AProblemDetailsResponse_CarriesEverySecurityHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        AssertSecurityHeaders(response);
    }

    //#endif
    [Fact]
    public async Task TheLivenessProbe_CarriesEverySecurityHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        AssertSecurityHeaders(response);
    }
    //#if (signedInRoute)

    [Fact]
    public async Task AnApiRoute_CarriesTheStrictContentSecurityPolicy()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        Policy(response).ShouldBe(SecurityHeaderOptions.DefaultContentSecurityPolicy);
        Policy(response).ShouldBe("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
    }
    //#endif

    [Fact]
    public async Task TheFramingControls_AreBothPresent()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);

        SecurityHeaderAssertions.Header(response, "X-Frame-Options").ShouldBe("DENY");
        Policy(response).ShouldContain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task NoResponse_NamesTheServerOrItsFramework()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);

        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
        response.Headers.Contains("X-AspNet-Version").ShouldBeFalse();
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response) =>
        SecurityHeaderAssertions.AssertSecurityHeaders(response);

    private static string Policy(HttpResponseMessage response) =>
        SecurityHeaderAssertions.Policy(response);
}

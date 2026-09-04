using System.Net;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The response-security headers must be on every response, not on the convenient ones.
/// </summary>
/// <remarks>
/// The three cases below are the three ways a response leaves this API, and they take three
/// different paths through the pipeline: a controller writing a result, the bearer handler writing a
/// ProblemDetails during authentication, and a health endpoint that no controller and no
/// authorisation ever sees. A middleware that set headers on the way in would pass the first and fail
/// the others, because <c>UseExceptionHandler</c> clears the response before re-running it.
/// </remarks>
public sealed class SecurityHeaderTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ASuccessfulResponse_CarriesEverySecurityHeader()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AProblemDetailsResponse_CarriesEverySecurityHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task TheLivenessProbe_CarriesEverySecurityHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        AssertSecurityHeaders(response);
    }

    /// <summary>
    /// The strict policy, asserted as the exact string. A partial assertion would pass on a policy
    /// that had quietly lost <c>default-src</c> and kept only <c>frame-ancestors</c>.
    /// </summary>
    [Fact]
    public async Task AnApiRoute_CarriesTheStrictContentSecurityPolicy()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        Policy(response).ShouldBe(SecurityHeaderOptions.DefaultContentSecurityPolicy);
        Policy(response).ShouldBe("default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
    }

    /// <summary>
    /// Both halves of the clickjacking control, so that dropping either one fails. The header is for
    /// agents predating CSP Level 2; the directive is what a current browser obeys.
    /// </summary>
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

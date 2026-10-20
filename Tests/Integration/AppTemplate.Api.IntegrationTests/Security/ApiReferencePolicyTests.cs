using System.Net;
using System.Text.RegularExpressions;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class ApiReferencePolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _pagePath = "/scalar/v1";

    [Fact]
    public async Task TheApiReferencePage_ServesItsDocumentAndNotMerelyA200()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(_pagePath, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        string page = await response.Content.ReadAsStringAsync(TestToken);

        page.ShouldContain("<div id=\"app\"></div>");
        page.ShouldContain("scalar.js");
        page.ShouldContain("scalar.aspnetcore.js");

        page.ShouldContain("initialize(");
    }

    [Fact]
    public async Task TheApiReferencePage_IsServedWithAPolicyThatAllowsItsAssets()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(_pagePath, UriKind.Relative), TestToken);

        string policy = Policy(response);

        policy.ShouldNotBe(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            "The strict API policy on this path serves a blank page.");

        policy.ShouldContain("script-src 'self' ");
        policy.ShouldContain("connect-src 'self'");

        policy.ShouldContain("style-src 'self' 'unsafe-inline'");

        policy.ShouldContain("img-src 'self' data: blob:");

        policy.ShouldContain("font-src https://fonts.scalar.com");

        policy.ShouldContain("worker-src 'self' blob:");

        policy.ShouldContain("default-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldContain("base-uri 'none'");
    }

    [Fact]
    public async Task TheInlineScript_RunsOnANonceThatMatchesTheHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(_pagePath, UriKind.Relative), TestToken);

        string policy = Policy(response);
        string page = await response.Content.ReadAsStringAsync(TestToken);

        ScriptSource(policy).ShouldNotContain("'unsafe-inline'");

        var declared = Regex.Match(policy, @"'nonce-(?<value>[^']+)'", RegexOptions.None, TimeSpan.FromSeconds(1));
        declared.Success.ShouldBeTrue($"No nonce in the policy: {policy}");

        string nonce = declared.Groups["value"].Value;
        nonce.ShouldNotBeNullOrWhiteSpace();

        var onTags = Regex.Matches(page, @"<script[^>]*\snonce=""(?<value>[^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1));

        onTags.Count.ShouldBe(
            3,
            $"The page renders three script tags and each must carry the nonce. Page:{Environment.NewLine}{page}");

        foreach (Match tag in onTags)
        {
            tag.Groups["value"].Value.ShouldBe(nonce);
        }
    }

    [Fact]
    public async Task TheApiReferenceAssets_AreServedFromTheSameOrigin()
    {
        var client = CreateClient();

        using var bundle = await client.GetAsync(new Uri("/scalar/scalar.js", UriKind.Relative), TestToken);
        using var loader = await client.GetAsync(new Uri("/scalar/scalar.aspnetcore.js", UriKind.Relative), TestToken);

        bundle.StatusCode.ShouldBe(HttpStatusCode.OK);
        loader.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await loader.Content.ReadAsStringAsync(TestToken)).ShouldContain("createApiReference");
    }

    [Fact]
    public async Task TheOpenApiDocument_IsReachableByThePage()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await response.Content.ReadAsStringAsync(TestToken)).ShouldContain("\"openapi\"");
    }

    private static string ScriptSource(string policy) =>
        policy.Split(';', StringSplitOptions.TrimEntries)
            .Single(directive => directive.StartsWith("script-src ", StringComparison.Ordinal));

    private static string Policy(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Content-Security-Policy", out var values)
            .ShouldBeTrue("The API-reference page carries no Content-Security-Policy at all.");

        return values!.Single();
    }
}

using System.Net;
using System.Text.RegularExpressions;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The API-reference page is the one document this origin serves, and a JSON API's default-deny policy
/// would render it blank. The exception is scoped to its path prefix and to Development, and this is
/// what proves the exception actually works rather than merely existing.
/// </summary>
/// <remarks>
/// The test host runs under Development — that is what maps the page at all — so these assertions
/// exercise the same branch a developer's <c>dotnet run</c> does.
/// </remarks>
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

        // The mount point and both script assets. A 200 with an empty body would pass a status check.
        page.ShouldContain("<div id=\"app\"></div>");
        page.ShouldContain("scalar.js");
        page.ShouldContain("scalar.aspnetcore.js");

        // The inline module script that the nonce exists for.
        page.ShouldContain("initialize(");
    }

    /// <summary>
    /// Every directive the page needs, each one asserted separately so that losing one is a distinct
    /// failure rather than a diff of one long string.
    /// </summary>
    [Fact]
    public async Task TheApiReferencePage_IsServedWithAPolicyThatAllowsItsAssets()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(_pagePath, UriKind.Relative), TestToken);

        string policy = Policy(response);

        policy.ShouldNotBe(
            "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
            "The strict API policy on this path serves a blank page.");

        // Its own two script files, and the bundle fetches the OpenAPI document.
        policy.ShouldContain("script-src 'self' ");
        policy.ShouldContain("connect-src 'self'");

        // The bundle mounts its stylesheet as a <style> element it creates itself.
        policy.ShouldContain("style-src 'self' 'unsafe-inline'");

        // favicon.svg, data: images inlined in the bundle's CSS, blob: response previews.
        policy.ShouldContain("img-src 'self' data: blob:");

        // The bundle's @font-face rules load Inter from this host.
        policy.ShouldContain("font-src https://fonts.scalar.com");

        // It starts a module worker from an object URL.
        policy.ShouldContain("worker-src 'self' blob:");

        // Relaxed for the page's own assets, never for framing or for a rewritten base URL.
        policy.ShouldContain("default-src 'none'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldContain("base-uri 'none'");
    }

    /// <summary>
    /// The page's single inline module script runs on a nonce, so <c>'unsafe-inline'</c> is never
    /// opened for scripts. The nonce is worth nothing unless the value in the header is the value on
    /// the tag, which is what this compares.
    /// </summary>
    [Fact]
    public async Task TheInlineScript_RunsOnANonceThatMatchesTheHeader()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(_pagePath, UriKind.Relative), TestToken);

        string policy = Policy(response);
        string page = await response.Content.ReadAsStringAsync(TestToken);

        // 'unsafe-inline' is needed for styles and must never be needed for scripts.
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

    /// <summary>
    /// A policy that permitted the page but not the files it loads would still show a blank page, so
    /// the assets themselves have to be reachable under the prefix.
    /// </summary>
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

    /// <summary>
    /// The document the page fetches under <c>connect-src 'self'</c>. Without it the page renders and
    /// then shows nothing, which a status-code assertion on the HTML would not notice.
    /// </summary>
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

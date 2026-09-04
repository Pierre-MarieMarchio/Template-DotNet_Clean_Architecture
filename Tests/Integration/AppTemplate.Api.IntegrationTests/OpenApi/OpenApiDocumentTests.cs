using System.Net;
using System.Text.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.OpenApi;

/// <summary>
/// The served OpenAPI document, asserted on its content rather than on its status code.
/// </summary>
/// <remarks>
/// <para>
/// A document endpoint fails quietly. The route matches, the body is valid OpenAPI, and the only
/// evidence that anything is wrong is a path that is not in it or a sentence with a hole in it —
/// neither of which a reachability check can see. That is the failure these tests are for, and it
/// is not hypothetical: <c>AppTemplate.Api.csproj</c> declines two analyzer suggestions on the
/// strength of the description assertion below.
/// </para>
/// <para>
/// The host runs under Development, which is the only environment that maps the document at all.
/// </para>
/// </remarks>
public sealed class OpenApiDocumentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _documentPath = "/openapi/v1.json";

    [Fact]
    public async Task TheDocument_IsNamedAfterTheApiAssemblyAndNotTheHost()
    {
        using var document = await GetDocumentAsync(_documentPath);

        document.RootElement.GetProperty("openapi").GetString().ShouldStartWith("3.1");

        // The test host's entry assembly is the test project, so a title taken from the entry
        // assembly rather than from the document's own would read "AppTemplate.Api.IntegrationTests
        // | v1" here and "AppTemplate.Api | v1" in production — a difference no test that runs only
        // in the test host would otherwise notice.
        document.RootElement.GetProperty("info").GetProperty("title").GetString()
            .ShouldBe("AppTemplate.Api | v1");
    }

    /// <summary>
    /// What AV0030 is about: one document per version, holding that version's actions and no others.
    /// </summary>
    [Fact]
    public async Task TheDocument_HoldsEveryVersionedRouteAndNothingUnversioned()
    {
        using var document = await GetDocumentAsync(_documentPath);

        string[] paths = [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name)];

        paths.ShouldNotBeEmpty();

        // Establishes the candidate set before asserting over it: an empty document would satisfy
        // "every path is a v1 path" without holding a single one.
        paths.Length.ShouldBeGreaterThan(30);

        paths.ShouldAllBe(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));

        // Both are mapped outside the ApiExplorer, so their absence is what proves the document is
        // built from the version's action list rather than from the route table.
        paths.ShouldNotContain("/health");
        paths.ShouldNotContain("/health/ready");

        paths.ShouldContain("/api/v1/auth/login");
        paths.ShouldContain(TodoListsRoute);
    }

    [Fact]
    public async Task AVersionWithNoActions_HasNoDocument()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v2.json", UriKind.Relative), TestToken);

        // A single document registered under every name would answer 200 here, with v1's paths in
        // it, and every assertion above would still pass.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The bearer scheme <c>OpenApiSecurityTransformer</c> declares, and the global requirement it
    /// deliberately does not add.
    /// </summary>
    [Fact]
    public async Task TheDocument_DeclaresTheBearerSchemeWithoutARequirementOnEveryOperation()
    {
        using var document = await GetDocumentAsync(_documentPath);

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        // Http rather than ApiKey: it is what makes the UI add the "Bearer " prefix itself.
        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        scheme.GetProperty("bearerFormat").GetString().ShouldBe("JWT");

        document.RootElement.TryGetProperty("security", out _).ShouldBeFalse(
            "A global security requirement would put a padlock on every operation, including the "
            + "anonymous ones, describing the document rather than the runtime.");
    }

    /// <summary>
    /// The assertion the two suppressed analyzers in <c>AppTemplate.Api.csproj</c> rest on.
    /// </summary>
    /// <remarks>
    /// <c>ResetPasswordRequest</c>'s summary is one sentence built around a
    /// <c>&lt;see cref="ConfirmEmailRequest"/&gt;</c>, so a renderer that drops the reference leaves
    /// "for the reason  gives." — grammatical wreckage in the published reference, and invisible to
    /// every other assertion in this file. 26 descriptions in this document are shaped that way.
    /// </remarks>
    [Fact]
    public async Task ASchemaDescription_ResolvesTheTypeThatItsSeeCrefNames()
    {
        using var document = await GetDocumentAsync(_documentPath);

        string? description = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ResetPasswordRequest")
            .GetProperty("description")
            .GetString();

        description.ShouldNotBeNull();
        description.ShouldContain("ConfirmEmailRequest");
    }

    private async Task<JsonDocument> GetDocumentAsync(string path)
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));
    }
}

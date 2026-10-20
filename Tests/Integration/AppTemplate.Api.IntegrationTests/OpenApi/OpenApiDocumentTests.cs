using System.Net;
using System.Text.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.OpenApi;

public sealed class OpenApiDocumentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _documentPath = "/openapi/v1.json";

    [Fact]
    public async Task TheDocument_IsNamedAfterTheApiAssemblyAndNotTheHost()
    {
        using var document = await GetDocumentAsync(_documentPath);

        document.RootElement.GetProperty("openapi").GetString().ShouldStartWith("3.1");

        document.RootElement.GetProperty("info").GetProperty("title").GetString()
            .ShouldBe("AppTemplate.Api | v1");
    }
    //#if (signedInRoute)

    [Fact]
    public async Task TheDocument_HoldsEveryVersionedRouteAndNothingUnversioned()
    {
        using var document = await GetDocumentAsync(_documentPath);

        string[] paths = [.. document.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name)];

        paths.ShouldNotBeEmpty();
        //#if (localAuth)

        paths.Length.ShouldBeGreaterThan(30);
        //#endif

        paths.ShouldAllBe(path => path.StartsWith("/api/v1/", StringComparison.Ordinal));

        paths.ShouldNotContain("/health");
        paths.ShouldNotContain("/health/ready");

        //#if (localAuth)
        paths.ShouldContain("/api/v1/auth/login");
        //#endif
        paths.ShouldContain(ProtectedRoute);
    }
    //#endif

    [Fact]
    public async Task AVersionWithNoActions_HasNoDocument()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/openapi/v2.json", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
    //#if (!publicApi)

    [Fact]
    public async Task TheDocument_DeclaresTheBearerSchemeWithoutARequirementOnEveryOperation()
    {
        using var document = await GetDocumentAsync(_documentPath);

        var scheme = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer");

        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        scheme.GetProperty("bearerFormat").GetString().ShouldBe("JWT");

        document.RootElement.TryGetProperty("security", out _).ShouldBeFalse(
            "A global security requirement would put a padlock on every operation, including the "
            + "anonymous ones, describing the document rather than the runtime.");
    }
    //#endif
    //#if (localAuth)

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
    //#endif

    private async Task<JsonDocument> GetDocumentAsync(string path)
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));
    }
}

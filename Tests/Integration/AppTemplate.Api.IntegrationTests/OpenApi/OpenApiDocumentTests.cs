using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task TheDocument_MatchesItsReviewedReference()
    {
        string reference = ReferencePath();

        File.Exists(reference).ShouldBeTrue(
            $"There is no reviewed OpenAPI reference at {reference}. Write it with " +
            "`dotnet run Tools/Tasks.cs openapi`, read it, and commit it.");

        var reviewed = JsonNode.Parse(await File.ReadAllTextAsync(reference, TestToken))!.AsObject();
        var live = JsonNode.Parse(await GetDocumentTextAsync(_documentPath))!.AsObject();

        reviewed.Remove("servers");
        live.Remove("servers");

        string[] differences = [.. Differences(reviewed, live, "$").Take(20)];

        differences.ShouldBeEmpty(
            "The contract a client builds on has changed. If the change is meant, run " +
            "`dotnet run Tools/Tasks.cs openapi` and commit the reference with it, so the review sees " +
            "what a client will see.");
    }

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

    [Theory]
    [InlineData("LoginResponseAuthenticated")]
    [InlineData("LoginResponseTwoFactorRequired")]
    [InlineData("ExternalLoginResponseAuthenticated")]
    [InlineData("ExternalLoginResponseTwoFactorRequired")]
    public async Task ABranchOfAPolymorphicResponse_RequiresItsDiscriminator(string schemaName)
    {
        using var document = await GetDocumentAsync(_documentPath);

        var schema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName);

        schema.GetProperty("properties").TryGetProperty("status", out _).ShouldBeTrue();

        string?[] required = [.. schema.GetProperty("required").EnumerateArray().Select(name => name.GetString())];

        required.ShouldContain("status", "a client narrows the answer on status, which it cannot do while status is optional.");
    }
    //#endif

    private async Task<JsonDocument> GetDocumentAsync(string path) =>
        JsonDocument.Parse(await GetDocumentTextAsync(path));

    private async Task<string> GetDocumentTextAsync(string path)
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync(TestToken);
    }

    private static IEnumerable<string> Differences(JsonNode? reviewed, JsonNode? live, string path)
    {
        if (reviewed is JsonObject before && live is JsonObject after)
        {
            return before.Select(property => property.Key)
                .Union(after.Select(property => property.Key), StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .SelectMany(name => Differences(before[name], after[name], $"{path}.{name}"));
        }

        if (reviewed is JsonArray first && live is JsonArray second && first.Count == second.Count)
        {
            return first.SelectMany((item, index) => Differences(item, second[index], $"{path}[{index}]"));
        }

        return JsonNode.DeepEquals(reviewed, live) ? [] : [$"{path}: {Shown(reviewed)} became {Shown(live)}"];
    }

    private static string Shown(JsonNode? node)
    {
        string text = node?.ToJsonString() ?? "absent";

        return text.Length > 80 ? text[..80] + "…" : text;
    }

    private static string ReferencePath()
    {
        string project = typeof(OpenApiDocumentTests).Assembly.GetName().Name!;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string folder = Path.Combine(directory.FullName, "Tests", "Integration", project);

            if (Directory.Exists(folder))
            {
                return Path.Combine(folder, "OpenApi", "v1.json");
            }
        }

        throw new InvalidOperationException($"No Tests/Integration/{project} above {AppContext.BaseDirectory}.");
    }
}

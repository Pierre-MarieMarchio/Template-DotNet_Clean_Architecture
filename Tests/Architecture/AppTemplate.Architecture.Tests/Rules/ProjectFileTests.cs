using System.Text.Json.Nodes;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ProjectFileTests
{
    private static readonly string[] _workspacePackages = ["api-client", "eslint-config", "tsconfig"];

    private static readonly Dictionary<string, string> _providerCalls = new(StringComparer.Ordinal)
    {
        ["postgresql"] = "UseNpgsql(",
        ["sqlserver"] = "UseSqlServer(",
        ["mysql"] = "UseMySql(",
        ["sqlite"] = "UseSqlite(",
    };

    private static string Root => ProjectReferenceGraph.RepositoryRoot;

    private static JsonObject ProjectFile =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "cleanarch.json"))) as JsonObject
        ?? throw new InvalidOperationException("cleanarch.json holds no JSON object.");

    private static string Name => Text(ProjectFile, "name");

    [Fact]
    public void TheProjectFile_NamesTheTemplateItWasGeneratedFrom()
    {
        JsonObject template = ProjectFile["template"] as JsonObject
            ?? throw new InvalidOperationException("cleanarch.json records no template.");

        Text(template, "package").ShouldNotBeEmpty();
        Text(template, "version").ShouldNotBeEmpty();
    }

    [Fact]
    public void TheName_IsTheSolutionsAndTheDomainProjects()
    {
        string[] solutions = [.. Directory.EnumerateFiles(Root, "*.sln").Select(Path.GetFileName).OfType<string>()];

        solutions.ShouldBe([Name + ".sln"], Case.Sensitive, $"cleanarch.json names the project {Name}, so its one solution is {Name}.sln.");
        Holds($"Src/Domain/{Name}.Domain").ShouldBeTrue($"cleanarch.json names the project {Name}, which has no Src/Domain/{Name}.Domain.");
    }

    [Fact]
    public void TheAuthentication_IsTheOneTheTreeHolds()
    {
        string auth = Option("auth");

        Holds($"Src/Application/{Name}.Application.Auth").ShouldBe(
            auth == "local",
            $"cleanarch.json records --auth {auth}, and the local accounts' project Src/Application/{Name}.Application.Auth comes with local alone.");
        Holds($"Src/Presentation/{Name}.Api/Features/ExternalIdentity").ShouldBe(
            auth == "external",
            $"cleanarch.json records --auth {auth}, and the external provider's mapping Src/Presentation/{Name}.Api/Features/ExternalIdentity comes with external alone.");
    }

    [Fact]
    public void TheDatabase_IsTheProviderTheContextsUse()
    {
        string database = Option("database");
        string provider = File.ReadAllText(Path.Combine(
            Root, "Src", "Infrastructure", $"{Name}.Infrastructure.Core", "Common", "Contexts", "DatabaseProvider.cs"));

        _providerCalls.Where(call => provider.Contains(call.Value, StringComparison.Ordinal)).Select(call => call.Key).ShouldBe(
            [database],
            Case.Sensitive,
            $"cleanarch.json records --database {database}, and DatabaseProvider.cs configures the contexts for another provider.");
    }

    [Theory]
    [InlineData("files", "Src/Infrastructure/{0}.Infrastructure.Storage")]
    [InlineData("ci", ".github/workflows/ci.yml")]
    [InlineData("sonar", "Tools/sonar-scanner.Dockerfile")]
    [InlineData("agentFiles", "AGENTS.md")]
    public void AnOptionThatAddsAPath_IsTrueExactlyWhenTheTreeHoldsIt(string option, string path)
    {
        string relative = string.Format(System.Globalization.CultureInfo.InvariantCulture, path, Name);

        Holds(relative).ShouldBe(Flag(option), $"cleanarch.json records options.{option} as {Flag(option)}, and {relative} comes with true alone.");
    }

    [Fact]
    public void TheJobs_RunWhereTheTreeHostsThem()
    {
        string jobs = Option("jobs");

        Holds($"Src/Presentation/{Name}.Worker").ShouldBe(
            jobs == "worker",
            $"cleanarch.json records --jobs {jobs}, and the worker Src/Presentation/{Name}.Worker comes with worker alone.");
    }

    [Fact]
    public void TheClient_IsRecordedExactlyWhenTheTreeHoldsIt()
    {
        bool client = ProjectFile["client"]?.GetValue<bool>() ?? false;

        Holds($"Src/Clients/{Name}.Client").ShouldBe(
            client,
            $"cleanarch.json records client as {client}; whatever adds Src/Clients/{Name}.Client records it as true.");
    }

    [Fact]
    public void TheWebWorkspace_IsRecordedWithEveryAppAndPackageItHolds()
    {
        JsonObject? web = ProjectFile["web"] as JsonObject;

        Holds("Src/Web").ShouldBe(web is not null, "cleanarch.json records a web workspace exactly when Src/Web holds one.");

        if (web is null)
        {
            return;
        }

        string scope = Text(web, "scope");
        string workspace = File.ReadAllText(Path.Combine(Root, "Src", "Web", "package.json"));
        workspace.ShouldContain($"\"name\": \"@{scope}/", Case.Sensitive, $"cleanarch.json records the web scope {scope}, which Src/Web/package.json does not name.");

        JsonObject[] apps = [.. (web["apps"] as JsonArray ?? []).OfType<JsonObject>()];
        Folders("Src/Web/apps").ShouldBe(
            [.. apps.Select(app => Text(app, "name")).Order(StringComparer.Ordinal)],
            Case.Sensitive,
            "cleanarch.json records other web apps than Src/Web/apps holds: web-join records one added with cleanarch-webapp.");

        foreach (JsonObject app in apps)
        {
            string name = Text(app, "name");
            string folder = Path.Combine(Root, "Src", "Web", "apps", name);

            Directory.EnumerateFiles(folder, "*.csproj").Any().ShouldBe(
                Text(app, "framework") == "blazor-wasm",
                $"cleanarch.json records {name} as a {Text(app, "framework")} app, and a .NET project comes with blazor-wasm alone.");

            if (Text(app, "framework") != "blazor-wasm")
            {
                File.Exists(Path.Combine(folder, "nginx.conf")).ShouldBe(
                    Text(app, "rendering") == "spa",
                    $"cleanarch.json records {name} as rendered {Text(app, "rendering")}, and an app served by nginx renders in the browser.");
            }
        }

        string[] packages = [.. (web["packages"] as JsonArray ?? []).Select(package => package!.GetValue<string>()).Order(StringComparer.Ordinal)];
        Folders("Src/Web/packages").Except(_workspacePackages, StringComparer.Ordinal).ShouldBe(
            packages,
            Case.Sensitive,
            "cleanarch.json records other web packages than the workspace was given: web-join records one added with cleanarch-webpackage.");
    }

    [Fact]
    public void ThePresentationProject_IsTheOneTheTreeHolds()
    {
        JsonObject? presentation = ProjectFile["presentation"] as JsonObject;
        string? name = presentation is null ? null : Text(presentation, "name");
        string[] own = [$"{Name}.Api", $"{Name}.Presentation.Core", $"{Name}.Worker"];
        string[] recorded = name is null ? [] : [$"{Name}.{name}", $"{Name}.{name}.Core"];

        Folders("Src/Presentation").Except(own, StringComparer.Ordinal).Except(recorded, StringComparer.Ordinal).ShouldBeEmpty(
            "Src/Presentation holds a presentation project cleanarch.json does not record: presentation-join records one added with cleanarch-presentation.");

        if (presentation is null)
        {
            return;
        }

        string folder = Path.Combine(Root, "Src", "Presentation", $"{Name}.{name}");
        Directory.Exists(folder).ShouldBeTrue($"cleanarch.json records the presentation project {name}, and Src/Presentation/{Name}.{name} does not exist.");

        string descriptor = Path.Combine(folder, "scaffold.json");
        if (File.Exists(descriptor))
        {
            Text((JsonObject)JsonNode.Parse(File.ReadAllText(descriptor))!, "framework").ShouldBe(
                Text(presentation, "framework"),
                $"cleanarch.json records {name} with another framework than its scaffold.json names.");
        }
    }

    /*#if (templateMaintenance)*/
    private static readonly string[] _sourcesOfOtherShapes =
    [
        "Src/Web",
        "Src/Clients/AppTemplate.Client",
        "Src/Presentation/AppTemplate.Api/Features/ExternalIdentity",
    ];

    private static bool Holds(string relative) =>
        !_sourcesOfOtherShapes.Contains(relative, StringComparer.Ordinal) && Path.Exists(Path.Combine(Root, relative));
    /*#else
    private static bool Holds(string relative) => Path.Exists(Path.Combine(Root, relative));
    #endif*/

    private static string[] Folders(string relative) =>
        Directory.Exists(Path.Combine(Root, relative))
            ? [.. Directory.EnumerateDirectories(Path.Combine(Root, relative)).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];

    private static string Option(string key) => ProjectFile["options"]?[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"cleanarch.json records no options.{key}.");

    private static bool Flag(string key) => ProjectFile["options"]?[key]?.GetValue<bool>()
        ?? throw new InvalidOperationException($"cleanarch.json records no options.{key}.");

    private static string Text(JsonObject json, string key) =>
        json[key]?.GetValue<string>() ?? throw new InvalidOperationException($"cleanarch.json records no {key}.");
}

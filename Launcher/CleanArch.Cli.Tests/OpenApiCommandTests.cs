using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class OpenApiCommandTests : IDisposable
{
    private const string _referenceJson = """{"openapi":"3.1.0"}""";

    private static readonly string _apiFolder = Path.Combine("Src", "Presentation", "AS.Api");
    private static readonly string _apiProject = Path.Combine(_apiFolder, "Acme.Shop.Api.csproj");
    private static readonly string _referencePath = Path.Combine("Tests", "Integration", "AS.Api.IntegrationTests", "OpenApi", "v1.json");
    private static readonly string _referenceDirectory = Path.GetDirectoryName(_referencePath)!;
    private static readonly string _clientFolder = Path.Combine("Src", "Presentation", "AS.Presentation.Client");
    private static readonly string _generated = Path.Combine(_clientFolder, "Generated");

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private int _exit = CleanArchCommand.Succeeded;
    private int _kiotaExit = CleanArchCommand.Succeeded;
    private bool _kiotaWritesItsLockFile = true;

    private readonly FakeProcesses _processes;

    public OpenApiCommandTests()
    {
        _processes = new FakeProcesses((call, _, _) =>
        {
            _calls.Add(call);
            return Fake(call);
        });
    }

    [Fact]
    public void OpenApi_WritesTheReference_ThenRegeneratesTheClient()
    {
        using var project = new SampleProject(SampleProject.Api);
        string cache = Path.Combine(project.Root, _apiFolder, "obj", "Acme.Shop.Api.OpenApiFiles.cache");
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, "cache");
        project.Folder(_clientFolder);

        Run(["openapi"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);

        ExternalCall build = _calls[0];
        build.FileName.ShouldBe("dotnet");
        build.WorkingDirectory.ShouldBe(project.Root);
        build.Arguments.ShouldBe(
        [
            "build", Path.Combine(project.Root, _apiProject),
            "-p:OpenApiGenerateDocumentsOnBuild=true",
            $"-p:OpenApiDocumentsDirectory={Path.Combine(project.Root, _referenceDirectory)}",
            "-p:OpenApiGenerateDocumentsOptions=--file-name v1",
        ]);
        build.Environment.ShouldNotBeNull()["ASPNETCORE_ENVIRONMENT"].ShouldBe("Development");
        build.Environment.ShouldNotBeNull().ContainsKey("Authentication__External__Authority").ShouldBeFalse();

        ExternalCall kiota = _calls[1];
        kiota.FileName.ShouldBe("dotnet");
        kiota.WorkingDirectory.ShouldBe(project.Root);
        kiota.Environment.ShouldBeNull();
        kiota.Arguments.ShouldBe(
        [
            "tool", "run", "kiota", "generate",
            "--language", "CSharp",
            "--openapi", Path.Combine(project.Root, _referencePath),
            "--output", Path.Combine(project.Root, _generated),
            "--class-name", "ApiClient",
            "--namespace-name", "Acme.Shop.Client.Generated",
            "--exclude-backward-compatible",
            "--clean-output",
            "--log-level", "Warning",
        ]);

        File.Exists(cache).ShouldBeFalse();
        File.ReadAllText(Path.Combine(project.Root, _referencePath)).ShouldBe(_referenceJson);

        string clientFile = Path.Combine(project.Root, _generated, "ApiClient.cs");
        Convert.ToHexString(File.ReadAllBytes(clientFile)[..3]).ShouldBe("EFBBBF");
        File.ReadAllText(clientFile).ShouldBe("namespace Acme.Shop.Client.Generated;\npublic class ApiClient { }\n");
        File.Exists(Path.Combine(project.Root, _generated, ".kiota.log")).ShouldBeFalse();
        JsonNode? settings = JsonNode.Parse(File.ReadAllText(Path.Combine(project.Root, _generated, "kiota-lock.json")));
        settings.ShouldNotBeNull()["descriptionLocation"].ShouldNotBeNull().GetValue<string>()
            .ShouldBe(Path.GetRelativePath(Path.Combine(project.Root, _generated), Path.Combine(project.Root, _referencePath)).Replace('\\', '/'));

        _output.ToString().ShouldContain($"Wrote {Shown(project.Root, Path.Combine(project.Root, _generated))} from the OpenAPI reference.");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void OpenApi_WithExternalAuth_RunsTheBuildWithTheAuthorityAndTheAudience()
    {
        using var project = new SampleProject(SampleProject.ApiWith("external", client: true));
        project.Folder(_clientFolder);

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        IReadOnlyDictionary<string, string> environment = _calls[0].Environment.ShouldNotBeNull();
        environment["ASPNETCORE_ENVIRONMENT"].ShouldBe("Development");
        environment["Authentication__External__Authority"].ShouldBe("https://identity-provider.invalid/");
        environment["Authentication__External__Audience"].ShouldBe("openapi-document");
        _output.ToString().ShouldStartWith(
            "ASPNETCORE_ENVIRONMENT=Development Authentication__External__Authority=https://identity-provider.invalid/ Authentication__External__Audience=openapi-document dotnet build ");
    }

    [Fact]
    public void OpenApi_ForwardsTheExitCodeOfTheBuild_AndStopsThere()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        _exit = 7;

        Run(["openapi"], project.Root).ShouldBe(7);

        _calls.Count.ShouldBe(1);
        _output.ToString().ShouldContain("dotnet build");
        _output.ToString().ShouldNotContain("kiota");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void OpenApi_ForwardsTheExitCodeOfKiota()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        _kiotaExit = 5;

        Run(["openapi"], project.Root).ShouldBe(5);

        _calls.Count.ShouldBe(2);
        _output.ToString().ShouldContain("kiota");
        _output.ToString().ShouldNotContain("Wrote");
    }

    [Fact]
    public void OpenApi_WritesTheClient_WhereKiotaWritesNoLockFile()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        _kiotaWritesItsLockFile = false;

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        File.Exists(Path.Combine(project.Root, _generated, "ApiClient.cs")).ShouldBeTrue();
        _output.ToString().ShouldContain("Wrote Src/Presentation/AS.Presentation.Client/Generated from the OpenAPI reference.");
    }

    [Fact]
    public void OpenApi_RefusesToWriteAClientTheProjectDoesNotHold()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _calls.Count.ShouldBe(1);
        _error.ToString().ShouldContain("Src/Presentation/AS.Presentation.Client does not exist, so there is no client to write");
        _output.ToString().ShouldNotContain("kiota");
    }

    [Fact]
    public void OpenApi_Check_WritesNothingAndSucceedsWhenTheTreeMatches()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        string written = SnapshotOf(project.Root);
        Clear();

        Run(["openapi", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);
        string prefix = "-p:OpenApiDocumentsDirectory=";
        string documents = _calls[0].Arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
        documents.ShouldStartWith(Path.GetTempPath());
        int output = _calls[1].Arguments.ToList().IndexOf("--output");
        string target = _calls[1].Arguments[output + 1];
        target.ShouldStartWith(Path.GetTempPath());

        _output.ToString().ShouldContain("is the OpenAPI reference the API describes itself with.");
        _output.ToString().ShouldContain("is what Kiota writes from the OpenAPI reference.");
        _output.ToString().ShouldNotContain("Wrote");
        SnapshotOf(project.Root).ShouldBe(written);
        Directory.Exists(documents).ShouldBeFalse();
        Directory.Exists(target).ShouldBeFalse();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void OpenApi_Check_FailsWhereTheReferenceWouldBeWritten()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        string reference = Path.Combine(project.Root, _referencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(reference)!);
        File.WriteAllText(reference, """{"openapi":"3.0.0"}""");

        Run(["openapi", "--check"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _calls.Count.ShouldBe(1);
        _error.ToString().ShouldContain("Tests/Integration/AS.Api.IntegrationTests/OpenApi/v1.json is not the reference the API describes itself with");
        _output.ToString().ShouldNotContain("kiota");
        File.ReadAllText(reference).ShouldBe("""{"openapi":"3.0.0"}""");
    }

    [Fact]
    public void OpenApi_Check_LeavesNoTemporaryFolderBehind_WhenTheBuildFails()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        _exit = 7;

        Run(["openapi", "--check"], project.Root).ShouldBe(7);

        string prefix = "-p:OpenApiDocumentsDirectory=";
        string documents = _calls.Single().Arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
        Directory.Exists(documents).ShouldBeFalse();
    }

    [Fact]
    public void OpenApi_Check_ReportsAReferenceThatIsMissing()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);

        Run(["openapi", "--check"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _calls.Count.ShouldBe(1);
        _error.ToString().ShouldContain("Tests/Integration/AS.Api.IntegrationTests/OpenApi/v1.json is missing: run `dotnet cleanarch openapi` first.");
        _output.ToString().ShouldNotContain("kiota");
    }

    [Fact]
    public void OpenApi_Check_FailsWhereTheClientWouldBeWritten()
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(_clientFolder);
        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        Clear();
        string clientFile = Path.Combine(project.Root, _generated, "ApiClient.cs");
        File.AppendAllText(clientFile, "// drifted");

        Run(["openapi", "--check"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _calls.Count.ShouldBe(2);
        _error.ToString().ShouldContain("Src/Presentation/AS.Presentation.Client/Generated differs from what Kiota writes from the OpenAPI reference:");
        _error.ToString().ShouldContain("  ApiClient.cs");
        _error.ToString().ShouldContain("Run `dotnet cleanarch openapi` and commit what it writes.");
        File.ReadAllText(clientFile).ShouldContain("// drifted");
    }

    [Fact]
    public void OpenApi_WithoutADotnetClient_WritesTheReference_AndRunsNoKiota()
    {
        using var project = new SampleProject(SampleProject.ApiWith("local", client: false));

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        File.ReadAllText(Path.Combine(project.Root, _referencePath)).ShouldBe(_referenceJson);
        _calls.ShouldNotContain(call => call.Arguments.Contains("kiota"));
    }

    [Fact]
    public void OpenApi_WithTheWebApiClientPackage_RegeneratesTheWebSchema_OrChecksItWithCheck()
    {
        using var project = new SampleProject(SampleProject.ApiWith("local", client: false));
        string package = project.Folder(Path.Combine("Src", "Web", "packages", "api-client"));
        Directory.CreateDirectory(Path.Combine(package, "scripts"));
        File.WriteAllText(Path.Combine(package, "scripts", "generate.mjs"), "");

        Run(["openapi"], project.Root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
        _calls[^1].Arguments.ShouldBe(["api", "--no-build"]);
        _calls[^1].WorkingDirectory.ShouldBe(Path.Combine(project.Root, "Src", "Web"));
        _calls.Clear();

        Run(["openapi", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
        _calls[^1].Arguments.ShouldBe(["exec", "node", "scripts/generate.mjs", "--check", Path.Combine(project.Root, _referencePath)]);
        _calls[^1].WorkingDirectory.ShouldBe(package);
    }

    [Fact]
    public void OpenApi_NoBuild_WritesFromTheApisLastBuild_AndRefusesWhereThereIsNone()
    {
        using var project = new SampleProject(SampleProject.ApiWith("local", client: false));

        Run(["openapi", "--no-build"], project.Root).ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain("has none");
        _calls.ShouldBeEmpty();

        File.WriteAllText(Path.Combine(project.Folder(Path.Combine(_apiFolder, "bin", "Debug", "net10.0")), "Acme.Shop.Api.dll"), "");
        Run(["openapi", "--no-build"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Single().Arguments.Take(3).ShouldBe(["msbuild", Path.Combine(project.Root, _apiProject), "-target:GenerateOpenApiDocuments"]);
        File.ReadAllText(Path.Combine(project.Root, _referencePath)).ShouldBe(_referenceJson);
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Fake(ExternalCall call)
    {
        if (call.Arguments[0] is "build" or "msbuild")
        {
            string prefix = "-p:OpenApiDocumentsDirectory=";
            string documents = call.Arguments.Single(argument => argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
            Directory.CreateDirectory(documents);
            File.WriteAllText(Path.Combine(documents, "v1.json"), _referenceJson);
        }

        if (call.Arguments.Contains("kiota"))
        {
            int output = call.Arguments.ToList().IndexOf("--output");
            string target = call.Arguments[output + 1];
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, ".kiota.log"), "kiota log");
            File.WriteAllText(Path.Combine(target, "ApiClient.cs"), "\uFEFFnamespace Acme.Shop.Client.Generated;\r\npublic class ApiClient { }\r\n");
            if (_kiotaWritesItsLockFile)
            {
                File.WriteAllText(Path.Combine(target, "kiota-lock.json"), "{\n  \"descriptionLocation\": \"https://example.invalid/v1.json\"\n}");
            }
        }

        return call.Arguments.Contains("kiota") ? _kiotaExit : _exit;
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _processes);

    private void Clear()
    {
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
        _calls.Clear();
    }

    private static string SnapshotOf(string root) =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(root, _generated), "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => Shown(root, file) + ":" + Convert.ToHexString(File.ReadAllBytes(file))));

    private static string Shown(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

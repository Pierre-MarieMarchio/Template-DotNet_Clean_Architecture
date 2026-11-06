using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.Shared;
using CleanArch.Cli.Tests.Generators;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class GeneratorCommandTests : IDisposable
{
    private const string _migrationCheck = "dotnet run Tools/Tasks.cs migration-check";

    private readonly string _scratch = Directory.CreateTempSubdirectory("cleanarch-cli-generators-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];

    public GeneratorCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return CleanArchCommand.Succeeded;
        };
    }

    [Fact]
    public void AddFeature_WithDryRun_PlansItsFilesInTheProjectsOfTheRepositorysOwnTree_AndWritesNothing()
    {
        Run(["add", "feature", "Gizmos", "Gizmo", "--access", "owner", "--deletion", "delete", "--dry-run"], Repository.Root)
            .ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        string[] planned = [.. _output.ToString().ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => (line.StartsWith("Src/", StringComparison.Ordinal) || line.StartsWith("Tests/", StringComparison.Ordinal)) && line.EndsWith(".cs", StringComparison.Ordinal))];

        planned.ShouldNotBeEmpty();
        planned.ShouldAllBe(path => path.Split('/', StringSplitOptions.None)[2].StartsWith("AT.", StringComparison.Ordinal));
        planned.Select(path => string.Join('/', path.Split('/').Take(3))).Distinct()
            .ShouldAllBe(project => Directory.Exists(Path.Combine(Repository.Root, project)));
        planned.ShouldAllBe(path => !File.Exists(Path.Combine(Repository.Root, path)));
        File.Exists(Path.Combine(Repository.Root, FeatureManifest.PathOf("Gizmos"))).ShouldBeFalse();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddFeature_WritesTheFeature_ThenRunsEachStepAtTheProjectRoot_AnnouncedOnce()
    {
        string root = Project(NewFeatureTests.NewTree(_scratch), auth: "local");

        Run(["add", "feature", "Widgets", "Widget", "--access", "shared"], Path.Combine(root, "Src")).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, FeatureManifest.PathOf("Widgets")))).ShouldNotBeNull().AsObject();
        manifest["generator"]!.GetValue<string>().ShouldBe("dotnet cleanarch add feature");
        manifest["generatorVersion"]!.GetValue<string>().ShouldBe(GeneratorCommand.Version);

        _calls.ShouldNotBeEmpty();
        _calls.ShouldAllBe(call => call.WorkingDirectory == root && call.FileName == "dotnet");
        string.Join(' ', _calls[0].Arguments).ShouldBe("run Tools/Tasks.cs migration-check");
        string[] lines = _output.ToString().ReplaceLineEndings("\n").Split('\n');
        lines.ShouldNotContain(_migrationCheck);
        lines.ShouldContain(line => line.EndsWith(_migrationCheck, StringComparison.Ordinal));
    }

    [Fact]
    public void AddFeatureAndAddField_AreRefusedWithTheReason_WhereTheProjectAuthenticatesNobody()
    {
        string root = Project(NewFeatureTests.NewTree(_scratch), auth: "none");
        Dictionary<string, string> before = Snapshot(root);

        Run(["add", "feature", "Widgets", "Widget"], root).ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain("This project has no add feature command");
        _error.ToString().ShouldContain("auth none");

        Run(["add", "field", "Widgets", "Budget:decimal=0"], root).ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain("This project has no add field command");

        Snapshot(root).ShouldBe(before);
    }

    [Fact]
    public void RemoveFeature_Reports_AndRemovesOnlyWithApply()
    {
        string root = RemoveFeatureTests.Tree(_scratch, tree =>
        {
            Project(tree, auth: "local");
            NewFeature.Run(["Widgets", "Widget", "--access", "shared", "--root", tree], TextWriter.Null, TextWriter.Null, _ => 0, NewFeatureTests.Version)
                .ShouldBe(CleanArchCommand.Succeeded);
        });
        Dictionary<string, string> generated = Snapshot(root);

        Run(["remove", "feature", "Widgets"], root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        _output.ToString().ShouldContain("Blockers: none.");
        Snapshot(root).ShouldBe(generated);
        _calls.ShouldBeEmpty();

        Run(["remove", "feature", "Widgets", "--apply"], root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        File.Exists(Path.Combine(root, FeatureManifest.PathOf("Widgets"))).ShouldBeFalse();
        _calls.ShouldNotBeEmpty();
        string.Join(' ', _calls[0].Arguments).ShouldBe("run Tools/Tasks.cs migration-check");
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
        Directory.Delete(_scratch, recursive: true);
    }

    private static string Project(string root, string auth)
    {
        File.WriteAllText(
            Path.Combine(root, ProjectLocator.FileName),
            $$"""
            {
              "template": { "package": "CleanArch.WebApi.Templates", "version": "1.2.3" },
              "name": "Acme",
              "options": { "auth": "{{auth}}", "database": "postgresql" },
              "presentation": [ { "name": "Api", "framework": "api" } ]
            }
            """);
        return root;
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error);
}

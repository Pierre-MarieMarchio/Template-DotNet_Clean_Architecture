using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.Shared;
using CleanArch.Cli.Tests.Generators;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class GeneratorCommandTests : IDisposable
{
    private const string _migrationCheck = "dotnet cleanarch migrations add --check";

    private readonly string _scratch = Directory.CreateTempSubdirectory("cleanarch-cli-generators-").FullName;
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly FakeProcesses _processes;

    public GeneratorCommandTests()
    {
        _processes = new FakeProcesses((call, output, _) =>
        {
            if (call.FileName == "git")
            {
                (int code, string read) = FakeGit.Read(call.WorkingDirectory, call.Arguments);
                output.Write(read);
                return code;
            }

            _calls.Add(call);
            Build(call);
            return CleanArchCommand.Succeeded;
        });
    }

    [Fact]
    public void AddFeature_WithDryRun_PlansItsFilesInTheProjectsOfTheRepositorysOwnTree_AndWritesNothing()
    {
        Run(["add", "feature", "Gizmos", "Gizmo", "--access", "owner", "--deletion", "delete", "--dry-run"], Repository.Root)
            .ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        string[] planned = [.. _output.ToString().ReplaceLineEndings("\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => (line.StartsWith("src/", StringComparison.Ordinal) || line.StartsWith("tests/", StringComparison.Ordinal)) && line.EndsWith(".cs", StringComparison.Ordinal))];

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

        Run(["add", "feature", "Widgets", "Widget", "--access", "shared"], Path.Combine(root, "src")).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        JsonObject manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, FeatureManifest.PathOf("Widgets")))).ShouldNotBeNull().AsObject();
        manifest["generator"]!.GetValue<string>().ShouldBe("dotnet cleanarch add feature");
        manifest["generatorVersion"]!.GetValue<string>().ShouldBe(GeneratorCommand.Version);

        _calls.ShouldNotBeEmpty();
        _calls.ShouldAllBe(call => call.WorkingDirectory == root && call.FileName == "dotnet");
        string.Join(' ', _calls[0].Arguments).ShouldStartWith("ef migrations has-pending-model-changes");
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
            GeneratorRun.Invoke(AddFeatureCommand.Build(GeneratorRun.Project(tree), _ => new FakeExecutor(_ => 0), NewFeatureTests.Version), "Widgets", "Widget", "--access", "shared")
                .Code.ShouldBe(CleanArchCommand.Succeeded);
        });
        Dictionary<string, string> generated = Snapshot(root);

        Run(["remove", "feature", "Widgets"], root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        _output.ToString().ShouldContain("Blockers: none.");
        Snapshot(root).ShouldBe(generated);
        _calls.ShouldBeEmpty();

        Run(["remove", "feature", "Widgets", "--apply"], root).ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        File.Exists(Path.Combine(root, FeatureManifest.PathOf("Widgets"))).ShouldBeFalse();
        _calls.ShouldNotBeEmpty();
        string.Join(' ', _calls[0].Arguments).ShouldStartWith("ef migrations has-pending-model-changes");
    }

    public void Dispose()
    {
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
        foreach (string page in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Where(page => !File.ReadAllText(page).StartsWith("---", StringComparison.Ordinal)))
        {
            File.WriteAllText(page, $"---\ntype: reference\ntitle: {Path.GetFileNameWithoutExtension(page)}\ndescription: A page.\n---\n\n{File.ReadAllText(page)}");
        }

        return root;
    }

    private static void Build(ExternalCall call)
    {
        if (call.Arguments is ["build", ..])
        {
            string built = Directory.CreateDirectory(Path.Combine(call.WorkingDirectory, "src", "Presentation", "A.Api", "bin")).FullName;
            File.WriteAllText(Path.Combine(built, "Acme.Api.dll"), string.Empty);
        }

        const string documents = "-p:OpenApiDocumentsDirectory=";
        if (call.Arguments.FirstOrDefault(argument => argument.StartsWith(documents, StringComparison.Ordinal)) is { } folder)
        {
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(folder[documents.Length..]).FullName, "v1.json"), "{}");
        }
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _processes);
}

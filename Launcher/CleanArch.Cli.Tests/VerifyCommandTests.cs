using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class VerifyCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private Func<ExternalCall, TextWriter, int>? _onCall;
    private string? _kiotaOutput;

    private static readonly string _pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
    private static readonly string _persistence = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Persistence");
    private static readonly string _auth = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Auth");
    private static readonly string _solution = "Acme.Shop.slnx";
    private static readonly string _reference = Path.Combine("Tests", "Integration", "AS.Api.IntegrationTests", "OpenApi", "v1.json");

    private static readonly CommandRegistration _parent = new(RefuseOutsideAProject, BuildParent);

    public VerifyCommandTests()
    {
        ExternalCommand.Handler = (call, output, _) =>
        {
            _calls.Add(call);
            if (call.Arguments.Contains("kiota"))
            {
                _kiotaOutput = OutputOf(call);
            }

            return _onCall?.Invoke(call, output) ?? CleanArchCommand.Succeeded;
        };
    }

    [Fact]
    public void Verify_LeavesOutTheTestsThatRunOnWindowsAlone_WhereItRunsElsewhere()
    {
        using var project = SampleSolution.WithSolution(SampleProject.WithOptions("local", sonar: false));
        AsBootstrapped(project, withAuth: true);
        SampleSolution.AddTestProject(project, "Tests/Windows", "A.Windows.csproj", SampleSolution.WindowsProject);
        string? runnable = null;
        _onCall = (call, _) =>
        {
            if (call.Arguments is ["test", "--solution", var filter, ..] && filter.EndsWith(".slnf", StringComparison.Ordinal))
            {
                runnable = File.ReadAllText(Path.Combine(call.WorkingDirectory, filter));
            }

            return CleanArchCommand.Succeeded;
        };

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        if (OperatingSystem.IsWindows())
        {
            runnable.ShouldBeNull();
            return;
        }

        _output.ToString().ShouldContain("Skipped Tests/Windows: it targets net10.0-windows and runs on Windows alone.");
        runnable.ShouldNotBeNull().ShouldContain("A.Unit.csproj");
        runnable.ShouldNotContain("A.Windows.csproj");
    }

    [Fact]
    public void Verify_PrintsEveryCommandItRuns_AndRunsEachAtTheProjectRoot()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));
        AsBootstrapped(project, withAuth: true);

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        string[] expected =
        [
            .. GateLines(),
            $"dotnet restore {_solution}",
            $"dotnet format {_solution} --diagnostics IDE0003 IDE0049 --verify-no-changes --no-restore",
            $"dotnet build {_solution} --configuration Debug --no-restore",
            $"dotnet test --solution {_solution} --configuration Debug --no-build",
            $"dotnet ef migrations has-pending-model-changes --project {_auth} --startup-project {_auth} --no-build",
            $"dotnet ef migrations has-pending-model-changes --project {_persistence} --startup-project {_persistence} --no-build",
            $"dotnet list {_solution} package --vulnerable --include-transitive",
        ];
        _output.ToString().ShouldBe(string.Join(Environment.NewLine, expected) + Environment.NewLine);
        _calls.Count.ShouldBe(expected.Length);
        _calls.ShouldAllBe(call => call.FileName == "dotnet" && call.WorkingDirectory == project.Root);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_StopsAtTheFirstFailingStep_AndForwardsItsExitCode()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));
        AsBootstrapped(project, withAuth: true);
        _onCall = (call, _) => call.Arguments is ["build", ..] ? 7 : CleanArchCommand.Succeeded;

        Run(["verify"], project.Root).ShouldBe(7);

        _calls.Count.ShouldBe(GateLines().Length + 3);
        _calls.Last().Arguments.ShouldBe(["build", _solution, "--configuration", "Debug", "--no-restore"]);
        _output.ToString().ShouldContain($"dotnet build {_solution}");
        _output.ToString().ShouldNotContain("dotnet test");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WithoutLocalAccounts_ChecksOnlyTheBusinessContext()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));
        AsBootstrapped(project, withAuth: false);

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        ExternalCall check = _calls.Single(call => call.Arguments.Contains("has-pending-model-changes"));
        check.Arguments.ShouldBe(["ef", "migrations", "has-pending-model-changes", "--project", _persistence, "--startup-project", _persistence, "--no-build"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WithoutTheInitialMigration_RefusesBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("needs the initial migration");
        _error.ToString().ShouldContain("AS.Infrastructure.Persistence");
        _error.ToString().ShouldContain("AS.Infrastructure.Auth");
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WithAWebWorkspace_RunsThePnpmStepsInSrcWeb()
    {
        using var project = new SampleProject(SampleProject.Web);
        AsBootstrapped(project, withAuth: true);

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        string web = Path.Combine(project.Root, "Src", "Web");
        ExternalCall[] pnpm = _calls.Where(call => call.FileName == _pnpm).ToArray();
        pnpm.Select(call => call.Arguments).ShouldBe(
        [
            ["--version"],
            ["audit"],
            ["install", "--frozen-lockfile"],
            ["lint"],
            ["test"],
            ["build"],
        ]);
        pnpm[0].WorkingDirectory.ShouldBe(project.Root);
        pnpm.Skip(1).ShouldAllBe(call => call.WorkingDirectory == web);
        _output.ToString().ShouldContain($"pnpm audit   (in Src/Web)");
        _output.ToString().ShouldContain("the web schema is not checked");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WithTheApiClientWebPackage_ChecksTheSchemaAgainstTheOpenApiReference()
    {
        using var project = new SampleProject(SampleProject.Web);
        AsBootstrapped(project, withAuth: true);
        string scripts = project.Folder(Path.Combine("Src", "Web", "packages", "api-client", "scripts"));
        File.WriteAllText(Path.Combine(scripts, "generate.mjs"), string.Empty);
        Directory.CreateDirectory(Path.Combine(project.Root, Path.GetDirectoryName(_reference)!));
        File.WriteAllText(Path.Combine(project.Root, _reference), "{ }");

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        ExternalCall[] pnpm = _calls.Where(call => call.FileName == _pnpm).ToArray();
        pnpm.Select(call => call.Arguments).ShouldBe(
        [
            ["--version"],
            ["install", "--frozen-lockfile"],
            ["exec", "node", "scripts/generate.mjs", "--check", _reference],
            ["audit"],
            ["install", "--frozen-lockfile"],
            ["lint"],
            ["test"],
            ["build"],
        ]);
        pnpm[2].WorkingDirectory.ShouldBe(Path.Combine(project.Root, "Src", "Web", "packages", "api-client"));
        _output.ToString().ShouldContain("(in Src/Web/packages/api-client)");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WithTheClient_ChecksWhatKiotaWritesAgainstTheGeneratedFolder()
    {
        using var project = new SampleProject(SampleProject.Client);
        AsBootstrapped(project, withAuth: false);
        string generated = WithClient(project);
        _onCall = (call, _) =>
        {
            if (call.Arguments.Contains("kiota"))
            {
                CopyTree(generated, OutputOf(call));
            }

            return CleanArchCommand.Succeeded;
        };

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        ExternalCall kiota = _calls.Single(call => call.Arguments.Contains("kiota"));
        kiota.Arguments.ShouldBe(
        [
            "tool", "run", "kiota", "generate",
            "--language", "CSharp",
            "--openapi", _reference,
            "--output", OutputOf(kiota),
            "--class-name", "ApiClient",
            "--namespace-name", "Acme.Shop.Client.Generated",
            "--exclude-backward-compatible",
            "--clean-output",
            "--log-level", "Warning",
        ]);
        kiota.WorkingDirectory.ShouldBe(project.Root);
        _output.ToString().ShouldContain("dotnet tool run kiota generate");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_WhenTheGeneratedClientDrifts_FailsAndNamesTheDriftedFile()
    {
        using var project = new SampleProject(SampleProject.Client);
        AsBootstrapped(project, withAuth: false);
        string generated = WithClient(project);
        _onCall = (call, _) =>
        {
            if (call.Arguments.Contains("kiota"))
            {
                string target = OutputOf(call);
                CopyTree(generated, target);
                File.AppendAllText(Path.Combine(target, "ApiClient.cs"), "// drifted\n");
            }

            return CleanArchCommand.Succeeded;
        };

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("differs from what Kiota writes from the OpenAPI reference");
        _error.ToString().ShouldContain("ApiClient.cs");
        _kiotaOutput.ShouldNotBeNull();
        Directory.Exists(_kiotaOutput).ShouldBeFalse("the temporary output is cleaned up");
    }

    [Fact]
    public void Verify_ForwardsTheExitCodeOfTheClientCheck_AndRunsNothingAfterIt()
    {
        using var project = new SampleProject(SampleProject.Client);
        AsBootstrapped(project, withAuth: false);
        WithClient(project);
        _onCall = (call, _) => call.Arguments.Contains("kiota") ? 9 : CleanArchCommand.Succeeded;

        Run(["verify"], project.Root).ShouldBe(9);

        _calls.Count.ShouldBe(GateLines().Length + 1);
        _calls.ShouldNotContain(call => call.Arguments.Contains("has-pending-model-changes"));
        _kiotaOutput.ShouldNotBeNull();
        Directory.Exists(_kiotaOutput).ShouldBeFalse();
    }

    [Fact]
    public void Verify_WithTheClientButNoOpenApiReference_FailsSayingToRunOpenapiFirst()
    {
        using var project = new SampleProject(SampleProject.Client);
        AsBootstrapped(project, withAuth: false);
        Directory.CreateDirectory(Path.Combine(project.Root, "Src", "Presentation", "AS.Presentation.Client", "Generated"));

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("v1.json is missing");
        _calls.ShouldNotContain(call => call.Arguments.Contains("kiota"));
    }

    [Fact]
    public void Verify_WithTheClientButNoClientFolder_FailsSayingSo()
    {
        using var project = new SampleProject(SampleProject.Client);
        AsBootstrapped(project, withAuth: false);

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine("Src", "Presentation", "AS.Presentation.Client"));
        _error.ToString().ShouldContain("does not exist");
        _calls.ShouldNotContain(call => call.Arguments.Contains("kiota"));
    }

    [Fact]
    public void Verify_InTheTemplateCheckout_RunsTheOptionalGatesTheLockfilesAndTheLauncherGate()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));
        AsBootstrapped(project, withAuth: true);
        string tools = project.Folder("Tools");
        File.WriteAllText(Path.Combine(tools, "CheckDocPaths.cs"), string.Empty);
        File.WriteAllText(Path.Combine(tools, "CheckWorkflows.cs"), string.Empty);
        File.WriteAllText(Path.Combine(tools, "CheckComments.cs"), string.Empty);
        File.WriteAllText(Path.Combine(project.Folder(".template.config"), "template.json"), "{}");
        File.WriteAllText(Path.Combine(project.Folder("Launcher"), "CleanArch.Create.slnx"), "<Solution />");

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        string[] lines = _output.ToString().Split(Environment.NewLine);
        foreach (string gate in new[] { "CheckDocPaths.cs", "CheckWorkflows.cs", "CheckComments.cs" })
        {
            lines.ShouldContain($"dotnet run {Path.Combine("Tools", gate)} --self-test");
            lines.ShouldContain($"dotnet run {Path.Combine("Tools", gate)} {project.Root}");
        }

        lines.ShouldContain($"dotnet run {Path.Combine("Tools", "Tasks.cs")} web-lockfiles --check");
        string launcher = Path.Combine("Launcher", "CleanArch.Create.slnx");
        lines.ShouldContain($"dotnet restore {launcher}");
        lines.ShouldContain($"dotnet format {launcher} --diagnostics IDE0003 IDE0049 --verify-no-changes --no-restore");
        lines.ShouldContain($"dotnet build {launcher} --configuration Debug --no-restore");
        lines.ShouldContain($"dotnet test --solution {launcher} --configuration Debug --no-build");
        _calls.Last().Arguments.ShouldBe(["test", "--solution", launcher, "--configuration", "Debug", "--no-build"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Verify_ForwardsTheExitCodeOfTheVulnerablePackagesListing()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));
        AsBootstrapped(project, withAuth: false);
        _onCall = (call, _) => call.Arguments.Contains("--vulnerable") ? 4 : CleanArchCommand.Succeeded;

        Run(["verify"], project.Root).ShouldBe(4);
    }

    [Fact]
    public void Verify_WhenTheListingNamesAVulnerablePackage_FailsEvenWithExitZero()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));
        AsBootstrapped(project, withAuth: false);
        _onCall = (call, output) =>
        {
            if (call.Arguments.Contains("--vulnerable"))
            {
                output.WriteLine("The project has the following vulnerable packages: Newtonsoft.Json 13.0.1.");
            }

            return CleanArchCommand.Succeeded;
        };

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("known vulnerability");
        _calls.Last().Arguments.ShouldContain("--vulnerable");
    }

    [Fact]
    public void OutsideAProject_VerifyIsRefusedWithTheReason()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["verify"], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void WithAProjectFileItCannotRead_VerifyIsRefusedWithThatError()
    {
        using var project = new SampleProject("{ not json");

        Run(["verify"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void Help_ListsVerifyInsideAProject_ButNotOutside()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));

        Run(["--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldContain("verify");
        Clear();

        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["--help"], folder).ShouldBe(CleanArchCommand.Succeeded);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _output.ToString().ShouldNotContain("verify");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ARefusedCommand_RefusesAndHidesItsSubcommandsToo_AndHelpJsonListsSubcommandsRecursively()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            CleanArchCommand.Run(["parent", "child"], folder, _output, _error, [HelpCommand.Registration, _parent])
                .ShouldBe(CleanArchCommand.Failed);
            _error.ToString().ShouldStartWith("Not inside a CleanArch project");
            Clear();

            CleanArchCommand.Run(["--help"], folder, _output, _error, [HelpCommand.Registration, _parent])
                .ShouldBe(CleanArchCommand.Succeeded);
            _output.ToString().ShouldNotContain("parent");
            Clear();
        }
        finally
        {
            Directory.Delete(folder);
        }

        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));
        CleanArchCommand.Run(["help", "--json"], project.Root, _output, _error, [HelpCommand.Registration, _parent])
            .ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        commands.Select(command => Text(command!["name"])).ShouldBe(["help", "parent"]);
        JsonObject parent = commands.Single(command => Text(command!["name"]) == "parent").ShouldNotBeNull().AsObject();
        parent["commands"].ShouldNotBeNull().AsArray().Select(command => Text(command!["name"])).ShouldBe(["child"]);
        _error.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private static readonly string[] _gateFiles =
    [
        "Tasks.cs", "NewFeature.cs", "NewUseCase.cs", "NewSetting.cs", "NewEvent.cs", "RemoveFeature.cs", "UpgradeReport.cs", "CoverageGate.cs",
    ];

    private static string[] GateLines() =>
    [
        .. _gateFiles.Select(gate => $"dotnet run {Path.Combine("Tools", gate)} --self-test"),
        $"dotnet run {Path.Combine("Tools", "Tasks.cs")} docs-index --check",
    ];

    private static void AsBootstrapped(SampleProject project, bool withAuth)
    {
        SampleSolution.AddTestProject(project, "Tests/Unit", "A.Unit.csproj", SampleSolution.UnitProject);
        Snapshot(Path.Combine(project.Root, _persistence));
        if (withAuth)
        {
            Snapshot(Path.Combine(project.Root, _auth));
        }
    }

    private static void Snapshot(string context)
    {
        string migrations = Path.Combine(context, "Migrations");
        Directory.CreateDirectory(migrations);
        File.WriteAllText(Path.Combine(migrations, "AcmeShopModelSnapshot.cs"), string.Empty);
    }

    private static string WithClient(SampleProject project)
    {
        string generated = Path.Combine(project.Root, "Src", "Presentation", "AS.Presentation.Client", "Generated");
        Directory.CreateDirectory(generated);
        File.WriteAllText(
            Path.Combine(generated, "ApiClient.cs"),
            "namespace Acme.Shop.Client.Generated;\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Directory.CreateDirectory(Path.Combine(project.Root, Path.GetDirectoryName(_reference)!));
        File.WriteAllText(Path.Combine(project.Root, _reference), "{ }");
        var settings = new JsonObject { ["descriptionLocation"] = Path.GetRelativePath(generated, Path.Combine(project.Root, _reference)).Replace('\\', '/') };
        File.WriteAllText(
            Path.Combine(generated, "kiota-lock.json"),
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }) + "\n");
        return generated;
    }

    private static string OutputOf(ExternalCall call) => call.Arguments[call.Arguments.ToList().IndexOf("--output") + 1];

    private static void CopyTree(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }
    }

    private JsonArray CommandsOf()
    {
        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        Clear();
        return document["commands"].ShouldNotBeNull().AsArray();
    }

    private void Clear()
    {
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
    }

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();

    private static string? RefuseOutsideAProject(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;

    private static Command BuildParent(CommandContext context)
    {
        var parent = new Command("parent", "A parent command.");
        var child = new Command("child", "A child command.");
        child.SetAction(_ => CleanArchCommand.Succeeded);
        parent.Add(child);
        parent.SetAction(_ => CleanArchCommand.Succeeded);
        return parent;
    }
}

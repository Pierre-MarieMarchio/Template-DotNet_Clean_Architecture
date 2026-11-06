using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class LifecycleCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly Queue<int> _exits = new();
    private int _exit = CleanArchCommand.Succeeded;

    private static readonly string _persistence = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Persistence");
    private static readonly string _auth = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Auth");
    private static readonly string _api = Path.Combine("Src", "Presentation", "AS.Api");

    public LifecycleCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return _exits.Count > 0 ? _exits.Dequeue() : _exit;
        };
    }

    [Fact]
    public void Bootstrap_PrintsEachLineItRuns_AndRunsThemAtTheProjectRoot()
    {
        using var project = WithSolution(SampleProject.WithOptions("local", sonar: false));

        Run(["bootstrap"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBe(
            $"dotnet format Acme.Shop.slnx --diagnostics IDE0055 IDE0005{Environment.NewLine}" +
            $"dotnet tool restore{Environment.NewLine}" +
            $"dotnet ef migrations add InitialCreate --project {_auth} --startup-project {_auth}{Environment.NewLine}" +
            $"dotnet ef migrations add InitialCreate --project {_persistence} --startup-project {_persistence}{Environment.NewLine}");
        _calls.Select(call => call.FileName).ShouldBe(["dotnet", "dotnet", "dotnet", "dotnet"]);
        _calls.Select(call => call.WorkingDirectory).ShouldBe([project.Root, project.Root, project.Root, project.Root]);
        _calls[2].Arguments.ShouldBe(["ef", "migrations", "add", "InitialCreate", "--project", _auth, "--startup-project", _auth]);
        _calls[3].Arguments.ShouldBe(["ef", "migrations", "add", "InitialCreate", "--project", _persistence, "--startup-project", _persistence]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Bootstrap_SkipsAContextThatAlreadyHasItsMigrations()
    {
        using var project = WithSolution(SampleProject.WithOptions("local", sonar: false));
        string migrations = project.Folder(Path.Combine(_persistence, "Migrations"));
        File.WriteAllText(Path.Combine(migrations, "Acme.Shop.PersistenceModelSnapshot.cs"), string.Empty);

        Run(["bootstrap"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(3);
        _calls[2].Arguments.ShouldContain(_auth);
        _output.ToString().ShouldContain($"AS.Infrastructure.Persistence already has its migrations");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Bootstrap_WithoutLocalAuth_CreatesOnlyTheBusinessContextInitialMigration()
    {
        using var project = WithSolution(SampleProject.WithOptions("none", sonar: false));

        Run(["bootstrap"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(3);
        _calls[2].Arguments.ShouldBe(["ef", "migrations", "add", "InitialCreate", "--project", _persistence, "--startup-project", _persistence]);
    }

    [Fact]
    public void Bootstrap_StopsAtTheFirstFailure_AndForwardsItsExitCode()
    {
        using var project = WithSolution(SampleProject.WithOptions("local", sonar: false));
        _exits.Enqueue(CleanArchCommand.Succeeded);
        _exits.Enqueue(5);

        Run(["bootstrap"], project.Root).ShouldBe(5);

        _calls.Count.ShouldBe(2);
        _output.ToString().ShouldContain("dotnet tool restore");
    }

    [Fact]
    public void Run_PrintsTheLineItRuns_AndForwardsItsExitCode()
    {
        using var project = new SampleProject(SampleProject.Api);
        _exit = 6;

        Run(["run"], project.Root).ShouldBe(6);

        _output.ToString().ShouldBe($"dotnet run --project {_api}{Environment.NewLine}");
        ExternalCall call = _calls.Single();
        call.FileName.ShouldBe("dotnet");
        call.WorkingDirectory.ShouldBe(project.Root);
        call.Arguments.ShouldBe(["run", "--project", _api]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Run_IsRefusedWhereTheProjectFileRecordsNoApiPresentation()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["run"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain($"its {ProjectLocator.FileName} records no presentation of framework api");
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
        Clear();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        CommandsOf().Select(command => Text(command!["name"])).Where(name => name is "run" or "end-to-end").ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ComposeUp_PrintsTheLineItRuns_AndForwardsItsExitCode()
    {
        using var project = new SampleProject(SampleProject.Full);
        _exit = 2;

        Run(["compose", "up"], project.Root).ShouldBe(2);

        _output.ToString().ShouldBe($"docker compose up -d --wait{Environment.NewLine}");
        ExternalCall call = _calls.Single();
        call.FileName.ShouldBe("docker");
        call.WorkingDirectory.ShouldBe(project.Root);
        call.Arguments.ShouldBe(["compose", "up", "-d", "--wait"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ComposeDown_PrintsTheLineItRuns_AtTheProjectRoot()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["compose", "down"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBe($"docker compose down{Environment.NewLine}");
        _calls.Single().Arguments.ShouldBe(["compose", "down"]);
        _calls.Single().WorkingDirectory.ShouldBe(project.Root);
    }

    [Fact]
    public void Compose_Alone_ShowsItsSubcommands()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["compose"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet cleanarch compose");
        _output.ToString().ShouldContain("up");
        _output.ToString().ShouldContain("down");
        _calls.ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void EndToEnd_IsRefusedWhereTheProjectFileRecordsNoApiPresentation()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: true));

        Run(["end-to-end"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain($"its {ProjectLocator.FileName} records no presentation of framework api");
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void Sonar_PrintsTheTwoDockerLines_AndRunsThemAtTheProjectRoot()
    {
        using var project = WithTests(SampleProject.WithOptions("local", sonar: true));

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);
        _calls[0].FileName.ShouldBe("docker");
        _calls[0].WorkingDirectory.ShouldBe(project.Root);
        _calls[0].Arguments.ShouldBe(["compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"]);
        _calls[1].FileName.ShouldBe("docker");
        _calls[1].Arguments.Take(9).ShouldBe(["compose", "--profile", "sonar", "run", "--rm", "--build", "sonar-scanner", "/bin/sh", "-c"]);
        string script = _calls[1].Arguments[9];
        script.ShouldContain($"dotnet test --project \"{Path.Combine("Tests", "Unit", "AS.Unit.Tests.csproj").Replace('\\', '/')}\"");
        script.ShouldContain("dotnet restore Acme.Shop.slnx --artifacts-path artifacts/sonar");
        script.ShouldNotContain("Integration");
        _output.ToString().ShouldContain("docker compose --profile sonar up --detach --wait sonarqube");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Sonar_ForwardsTheExitCodeOfTheSonarQubeService()
    {
        using var project = WithTests(SampleProject.WithOptions("local", sonar: true));
        _exit = 8;

        Run(["sonar"], project.Root).ShouldBe(8);

        _calls.Single().Arguments.ShouldBe(["compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube"]);
    }

    [Fact]
    public void Sonar_IsHiddenAndRefusedWhereTheProjectFileRecordsSonarFalse()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain($"its {ProjectLocator.FileName} records sonar false");
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
        Clear();

        Run(["--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldNotContain("sonar");
        Clear();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        CommandsOf().Select(command => Text(command!["name"])).ShouldNotContain("sonar");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Sonar_IsListedInHelpJson_WhenTheProjectFileRecordsSonarTrue()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: true));

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        CommandsOf().Select(command => Text(command!["name"])).ShouldContain("sonar");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsTheLifecycleCommands_AndTheSubcommandsOfCompose()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        commands.Select(command => Text(command!["name"])).Where(name => name is "bootstrap" or "run" or "compose" or "end-to-end" or "sonar").ShouldBe(["bootstrap", "run", "compose", "end-to-end", "sonar"]);
        JsonObject compose = commands.Single(command => Text(command!["name"]) == "compose").ShouldNotBeNull().AsObject();
        compose["commands"].ShouldNotBeNull().AsArray().Select(command => Text(command!["name"])).ShouldBe(["up", "down"]);
        JsonObject endToEnd = commands.Single(command => Text(command!["name"]) == "end-to-end").ShouldNotBeNull().AsObject();
        endToEnd["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--keep-stack", "--configuration"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Help_OutsideAProject_HidesTheRefusedCommands()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["--help"], folder).ShouldBe(CleanArchCommand.Succeeded);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _output.ToString().ShouldContain("help");
        _output.ToString().ShouldNotContain("bootstrap");
        _output.ToString().ShouldNotContain("compose");
        _output.ToString().ShouldNotContain("end-to-end");
        _output.ToString().ShouldNotContain("sonar");
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("run")]
    [InlineData("compose up")]
    [InlineData("compose down")]
    [InlineData("end-to-end")]
    [InlineData("sonar")]
    public void OutsideAProject_TheCommandIsRefusedWithTheReason(string commandLine)
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(commandLine.Split(' '), folder).ShouldBe(CleanArchCommand.Failed);
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

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("run")]
    [InlineData("compose up")]
    [InlineData("compose down")]
    [InlineData("end-to-end")]
    [InlineData("sonar")]
    public void WithAProjectFileItCannotRead_TheCommandIsRefusedWithThatError(string commandLine)
    {
        using var project = new SampleProject("{ not json");

        Run(commandLine.Split(' '), project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

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

    private static SampleProject WithSolution(string json)
    {
        var project = new SampleProject(json);
        File.WriteAllText(Path.Combine(project.Root, "Acme.Shop.slnx"), string.Empty);
        return project;
    }

    private static SampleProject WithTests(string json)
    {
        var project = new SampleProject(json);
        string unit = project.Folder(Path.Combine("Tests", "Unit"));
        File.WriteAllText(
            Path.Combine(unit, "AS.Unit.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        string integration = project.Folder(Path.Combine("Tests", "Integration"));
        File.WriteAllText(
            Path.Combine(integration, "AS.Integration.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include=\"Testcontainers\" /></ItemGroup></Project>");
        return project;
    }
}

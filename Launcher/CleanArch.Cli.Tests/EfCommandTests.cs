using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class EfCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private int _exit = CleanArchCommand.Succeeded;

    private static readonly string _persistence = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Persistence");
    private static readonly string _auth = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Auth");

    public EfCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return _exit;
        };
    }

    [Fact]
    public void MigrationsAdd_PrintsTheLineItRuns_AndRunsItAtTheProjectRoot()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations", "add", "AddTodoItemPriority"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBe($"dotnet ef migrations add AddTodoItemPriority --project {_persistence} --startup-project {_persistence}{Environment.NewLine}");
        ExternalCall call = _calls.Single();
        call.FileName.ShouldBe("dotnet");
        call.WorkingDirectory.ShouldBe(project.Root);
        call.Arguments.ShouldBe(["ef", "migrations", "add", "AddTodoItemPriority", "--project", _persistence, "--startup-project", _persistence]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void MigrationsAdd_ForwardsTheExitCode()
    {
        using var project = new SampleProject(SampleProject.Full);
        _exit = 7;

        Run(["migrations", "add", "AddTodoItemPriority"], project.Root).ShouldBe(7);

        _output.ToString().ShouldContain($"dotnet ef migrations add AddTodoItemPriority --project {_persistence} --startup-project {_persistence}");
        _calls.Single().Arguments.ShouldContain("AddTodoItemPriority");
    }

    [Fact]
    public void MigrationsAdd_Check_RunsHasPendingModelChanges_InsteadOfWriting()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations", "add", "AddTodoItemPriority", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldBe($"dotnet ef migrations has-pending-model-changes --project {_persistence} --startup-project {_persistence}{Environment.NewLine}");
        _calls.Single().Arguments.ShouldBe(["ef", "migrations", "has-pending-model-changes", "--project", _persistence, "--startup-project", _persistence]);
    }

    [Fact]
    public void MigrationsAdd_ContextAuth_TargetsTheIdentityContext()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations", "add", "AddAccessKeys", "--context", "auth"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain($"dotnet ef migrations add AddAccessKeys --project {_auth} --startup-project {_auth}");
        _calls.Single().Arguments.ShouldBe(["ef", "migrations", "add", "AddAccessKeys", "--project", _auth, "--startup-project", _auth]);
    }

    [Fact]
    public void MigrationsAdd_ContextAuth_IsRefusedWhereTheProjectKeepsNoAccounts()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));

        Run(["migrations", "add", "AddAccessKeys", "--context", "auth"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("--auth local");
        _calls.ShouldBeEmpty();
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void MigrationsAdd_AnUnknownContext_IsRefused()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations", "add", "AddTodoItemPriority", "--context", "billing"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("'billing' is not a context of this project");
        _calls.ShouldBeEmpty();
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void MigrationsBundle_RunsAgainstEachContext_AtTheProjectRoot()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations", "bundle"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Count.ShouldBe(2);
        _calls[0].FileName.ShouldBe("dotnet");
        _calls[0].WorkingDirectory.ShouldBe(project.Root);
        _calls[0].Arguments.ShouldBe(["ef", "migrations", "bundle", "--project", _auth, "--startup-project", _auth, "--configuration", "Debug", "--self-contained", "--force", "--output", Path.Combine("artifacts", "migrate", "efbundle-identity")]);
        _calls[1].Arguments.ShouldBe(["ef", "migrations", "bundle", "--project", _persistence, "--startup-project", _persistence, "--configuration", "Debug", "--self-contained", "--force", "--output", Path.Combine("artifacts", "migrate", "efbundle")]);
        _output.ToString().ShouldContain($"dotnet ef migrations bundle --project {_auth} --startup-project {_auth} --configuration Debug --self-contained --force --output {Path.Combine("artifacts", "migrate", "efbundle-identity")}");
        _output.ToString().ShouldContain($"dotnet ef migrations bundle --project {_persistence} --startup-project {_persistence} --configuration Debug --self-contained --force --output {Path.Combine("artifacts", "migrate", "efbundle")}");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void MigrationsBundle_WithoutLocalAuth_BundlesOnlyTheBusinessContext()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));

        Run(["migrations", "bundle"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Single().Arguments.ShouldBe(["ef", "migrations", "bundle", "--project", _persistence, "--startup-project", _persistence, "--configuration", "Debug", "--self-contained", "--force", "--output", Path.Combine("artifacts", "migrate", "efbundle")]);
    }

    [Fact]
    public void MigrationsBundle_StopsAtTheFirstFailure_AndForwardsItsExitCode()
    {
        using var project = new SampleProject(SampleProject.Full);
        _exit = 3;

        Run(["migrations", "bundle"], project.Root).ShouldBe(3);

        _calls.Count.ShouldBe(1);
    }

    [Fact]
    public void DatabaseUpdate_PrintsTheLineItRuns_AndForwardsTheExitCode()
    {
        using var project = new SampleProject(SampleProject.Full);
        _exit = 5;

        Run(["database", "update"], project.Root).ShouldBe(5);

        _output.ToString().ShouldBe($"dotnet ef database update --project {_persistence} --startup-project {_persistence}{Environment.NewLine}");
        ExternalCall call = _calls.Single();
        call.WorkingDirectory.ShouldBe(project.Root);
        call.Arguments.ShouldBe(["ef", "database", "update", "--project", _persistence, "--startup-project", _persistence]);
    }

    [Fact]
    public void DatabaseUpdate_ContextAuth_TargetsTheIdentityContext()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["database", "update", "--context", "auth"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain($"dotnet ef database update --project {_auth} --startup-project {_auth}");
    }

    [Theory]
    [InlineData("migrations add X")]
    [InlineData("migrations bundle")]
    [InlineData("database update")]
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
    [InlineData("migrations add X")]
    [InlineData("migrations bundle")]
    [InlineData("database update")]
    public void WithAProjectFileItCannotRead_TheCommandIsRefusedWithThatError(string commandLine)
    {
        using var project = new SampleProject("{ not json");

        Run(commandLine.Split(' '), project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        _output.ToString().ShouldBeEmpty();
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsTheCommands_TheirSubcommandsAndOptions()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        commands.Select(command => Text(command!["name"])).Where(name => name is "migrations" or "database").ShouldBe(["migrations", "database"]);
        JsonObject migrations = commands.Single(command => Text(command!["name"]) == "migrations").ShouldNotBeNull().AsObject();
        JsonArray subcommands = migrations["commands"].ShouldNotBeNull().AsArray();
        subcommands.Select(command => Text(command!["name"])).ShouldBe(["add", "bundle"]);
        JsonObject add = subcommands.Single(command => Text(command!["name"]) == "add").ShouldNotBeNull().AsObject();
        add["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--check", "--context"]);
        add["arguments"].ShouldNotBeNull().AsArray().Select(argument => Text(argument!["name"])).ShouldBe(["name"]);
        JsonObject database = commands.Single(command => Text(command!["name"]) == "database").ShouldNotBeNull().AsObject();
        JsonObject update = database["commands"].ShouldNotBeNull().AsArray().Single().ShouldNotBeNull().AsObject();
        Text(update["name"]).ShouldBe("update");
        update["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--context"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_HidesTheContextOption_WhereTheProjectKeepsNoAccounts()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        JsonObject migrations = commands.Single(command => Text(command!["name"]) == "migrations").ShouldNotBeNull().AsObject();
        JsonObject add = migrations["commands"].ShouldNotBeNull().AsArray().Single(command => Text(command!["name"]) == "add").ShouldNotBeNull().AsObject();
        add["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--check"]);
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
        _output.ToString().ShouldNotContain("migrations");
        _output.ToString().ShouldNotContain("database");
    }

    [Fact]
    public void Migrations_Alone_ShowsTheSubcommands()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["migrations"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("add <name>");
        _output.ToString().ShouldContain("bundle");
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

    private JsonArray CommandsOf()
    {
        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        _output.GetStringBuilder().Clear();
        return document["commands"].ShouldNotBeNull().AsArray();
    }

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();
}

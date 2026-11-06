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
    private Action<ExternalCall> _sideEffect = _ => { };

    private static readonly string _persistence = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Persistence");
    private static readonly string _auth = Path.Combine("Src", "Infrastructure", "AS.Infrastructure.Auth");

    public EfCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            _sideEffect(call);
            return _exit;
        };
    }

    [Fact]
    public void MigrationsAdd_ContextAuth_TargetsTheIdentityContext()
    {
        using var project = new SampleProject(SampleProject.Full);
        WriteSnapshot(project, _auth);

        Run(["migrations", "add", "AddAccessKeys", "--context", "auth"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

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
    public void MigrationsBundle_WithoutLocalAuth_BundlesOnlyTheBusinessContext_InTheConfigurationGiven()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));
        WriteSnapshot(project, _persistence);

        Run(["migrations", "bundle", "--configuration", "Release"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        List<string> arguments = [.. _calls.Single().Arguments];
        arguments[arguments.IndexOf("--project") + 1].ShouldBe(_persistence);
        arguments[arguments.IndexOf("--configuration") + 1].ShouldBe("Release");
    }

    [Fact]
    public void MigrationsBundle_StopsAtTheFirstFailure_AndForwardsItsExitCode()
    {
        using var project = new SampleProject(SampleProject.Full);
        WriteSnapshot(project, _auth);
        WriteSnapshot(project, _persistence);
        _exit = 3;

        Run(["migrations", "bundle"], project.Root).ShouldBe(3);

        _calls.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("migrations add AddAccessKeys --context auth")]
    [InlineData("migrations add --check --context auth")]
    [InlineData("migrations bundle")]
    [InlineData("database update --context auth")]
    public void WhereTheTargetedContextHasNoInitialMigration_TheCommandIsRefused_AndRunsNothing(string commandLine)
    {
        using var project = new SampleProject(SampleProject.Full);
        WriteSnapshot(project, _persistence);

        Run(commandLine.Split(' '), project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("needs the initial migration of AS.Infrastructure.Auth, and there is none yet");
        _error.ToString().ShouldContain("dotnet cleanarch bootstrap");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void MigrationsAdd_WhereTheMigrationWrittenIsEmpty_RemovesItRestoresTheSnapshotAndFails()
    {
        using var project = new SampleProject(SampleProject.Full);
        string snapshot = WriteSnapshot(project, _persistence);
        byte[] original = File.ReadAllBytes(snapshot);
        string migrations = Path.GetDirectoryName(snapshot)!;
        _sideEffect = _ => WriteMigration(migrations, "20261106120000_Nothing", _empty, snapshot);

        Run(["migrations", "add", "Nothing"], project.Root).ShouldBe(CleanArchCommand.Failed);

        Directory.GetFiles(migrations).ShouldBe([snapshot]);
        File.ReadAllBytes(snapshot).ShouldBe(original);
        _error.ToString().ShouldContain("The model of AS.Infrastructure.Persistence has not changed since its last migration");
    }

    [Fact]
    public void MigrationsAdd_WhereTheMigrationWrittenHoldsAChange_KeepsItAndTheNewSnapshot()
    {
        using var project = new SampleProject(SampleProject.Full);
        string snapshot = WriteSnapshot(project, _persistence);
        string migrations = Path.GetDirectoryName(snapshot)!;
        _sideEffect = _ => WriteMigration(migrations, "20261106120000_AddX", _addsAColumn, snapshot);

        Run(["migrations", "add", "AddX"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Directory.GetFiles(migrations).Length.ShouldBe(3);
        File.ReadAllText(snapshot).ShouldContain("changed");
        _error.ToString().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(_empty, true)]
    [InlineData(_addsAColumn, false)]
    [InlineData(_dropsOnly, false)]
    public void IsEmptyMigration_HoldsOnlyWhereUpAndDownAreBothEmpty(string source, bool empty)
    {
        MigrationsCommand.IsEmptyMigration(source).ShouldBe(empty);
        MigrationsCommand.IsEmptyMigration(source.Replace("\n", "\r\n", StringComparison.Ordinal)).ShouldBe(empty);
    }

    [Fact]
    public void MigrationsAdd_Check_NeedsNoName()
    {
        using var project = new SampleProject(SampleProject.Full);
        WriteSnapshot(project, _persistence);

        Run(["migrations", "add", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.Single().Arguments.Take(3).ShouldBe(["ef", "migrations", "has-pending-model-changes"]);
    }

    [Fact]
    public void MigrationsAdd_WithoutCheck_IsRefusedWithoutAName_AndRunsNothing()
    {
        using var project = new SampleProject(SampleProject.Full);
        WriteSnapshot(project, _persistence);

        Run(["migrations", "add"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("migrations add needs the migration's name");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_HidesTheContextOption_WhereTheProjectKeepsNoAccounts()
    {
        using var project = new SampleProject(SampleProject.WithOptions("none", sonar: false));

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        JsonObject migrations = commands.Single(command => Text(command!["name"]) == "migrations").ShouldNotBeNull().AsObject();
        JsonObject add = migrations["commands"].ShouldNotBeNull().AsArray().Single(command => Text(command!["name"]) == "add").ShouldNotBeNull().AsObject();
        add["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldNotContain("--context");
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private static string WriteSnapshot(SampleProject project, string context)
    {
        string snapshot = Path.Combine(project.Folder(Path.Combine(context, "Migrations")), "AppDbContextModelSnapshot.cs");
        File.WriteAllText(snapshot, "partial class AppDbContextModelSnapshot { }\n");
        return snapshot;
    }

    private static void WriteMigration(string folder, string name, string source, string snapshot)
    {
        File.WriteAllText(Path.Combine(folder, name + ".cs"), source);
        File.WriteAllText(Path.Combine(folder, name + ".Designer.cs"), "partial class Designer { }\n");
        File.WriteAllText(snapshot, "partial class AppDbContextModelSnapshot { /* changed */ }\n");
    }

    private const string _empty = """
        public partial class Nothing : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
            }
        }
        """;

    private const string _addsAColumn = """
        public partial class AddX : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<string>(name: "X", table: "Notes", nullable: true);
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
            }
        }
        """;

    private const string _dropsOnly = """
        public partial class DropX : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropColumn(name: "X", table: "Notes");
            }
        }
        """;

    private JsonArray CommandsOf()
    {
        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        _output.GetStringBuilder().Clear();
        return document["commands"].ShouldNotBeNull().AsArray();
    }

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();
}

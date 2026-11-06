using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class HelpCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public void Help_ShowsTheCommandsOfTheTool_InsideAProject()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["help"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet cleanarch [command] [options]");
        _output.ToString().ShouldContain("help");
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsTheCommandsAndOptionsThatApplyToTheProject()
    {
        using var project = new SampleProject(SampleProject.Full);

        Run(["help", "--json"], project.Folder("Src/Web/apps")).ShouldBe(CleanArchCommand.Succeeded);

        JsonObject document = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject();
        Text(document["command"]).ShouldBe("dotnet cleanarch");

        JsonObject found = document["project"].ShouldNotBeNull().AsObject();
        Text(found["root"]).ShouldBe(project.Root);
        Text(found["name"]).ShouldBe("Acme.Shop");
        Text(found["template"].ShouldNotBeNull().AsObject()["version"]).ShouldBe("1.2.3");
        Flag(found["client"]).ShouldBeTrue();
        Text(found["web"].ShouldNotBeNull().AsObject()["scope"]).ShouldBe("acme-shop");
        Text(found["presentation"].ShouldNotBeNull().AsArray().Single().ShouldNotBeNull().AsObject()["name"]).ShouldBe("Portal");

        JsonObject options = found["options"].ShouldNotBeNull().AsObject();
        Text(options["auth"]).ShouldBe("local");
        Text(options["database"]).ShouldBe("postgresql");
        Text(options["examples"]).ShouldBe("all");
        Flag(options["files"]).ShouldBeTrue();
        Flag(options["ci"]).ShouldBeTrue();
        Flag(options["agentFiles"]).ShouldBeTrue();
        options.ContainsKey("sonar").ShouldBeFalse("the project file records --sonar false, so sonar does not apply to this project");

        document["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"]))
            .ShouldBe(["--help", "--version"], ignoreOrder: true);

        JsonArray commands = document["commands"].ShouldNotBeNull().AsArray();
        JsonObject help = commands.Single(command => Text(command!["name"]) == "help").ShouldNotBeNull().AsObject();
        help["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"]))
            .ShouldBe(["--json"]);
        JsonObject format = commands.Single(command => Text(command!["name"]) == "format").ShouldNotBeNull().AsObject();
        format["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"]))
            .ShouldBe(["--check"]);
        JsonObject compose = commands.Single(command => Text(command!["name"]) == "compose").ShouldNotBeNull().AsObject();
        compose["commands"].ShouldNotBeNull().AsArray().Select(subcommand => Text(subcommand!["name"]))
            .ShouldBe(["up", "down"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsSonarOnlyWhenTheProjectFileMakesItTrue()
    {
        using var withSonar = new SampleProject(SampleProject.WithOptions("local", sonar: true));
        using var withoutSonar = new SampleProject(SampleProject.WithOptions("local", sonar: false));

        Run(["help", "--json"], withSonar.Root).ShouldBe(CleanArchCommand.Succeeded);
        JsonObject listed = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject()["project"].ShouldNotBeNull().AsObject()["options"].ShouldNotBeNull().AsObject();
        Flag(listed["sonar"]).ShouldBeTrue();
        _output.GetStringBuilder().Clear();

        Run(["help", "--json"], withoutSonar.Root).ShouldBe(CleanArchCommand.Succeeded);
        JsonObject omitted = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject()["project"].ShouldNotBeNull().AsObject()["options"].ShouldNotBeNull().AsObject();
        omitted.ContainsKey("sonar").ShouldBeFalse();
    }

    [Fact]
    public void HelpJson_OmitsWhatTheProjectFileLeavesOut()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonObject found = JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject()["project"].ShouldNotBeNull().AsObject();
        found.ContainsKey("client").ShouldBeFalse("the minimal project file holds no client key");
        found.ContainsKey("web").ShouldBeFalse();
        found.ContainsKey("presentation").ShouldBeFalse();
        found["options"].ShouldNotBeNull().AsObject().Select(pair => pair.Key).ShouldBe(["auth"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_RefusesOutsideAProject()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["help", "--json"], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ReportsAProjectFileItCannotRead()
    {
        using var project = new SampleProject("{ not json");

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();

    private static bool Flag(JsonNode? node) => node.ShouldNotBeNull().GetValue<bool>();
}

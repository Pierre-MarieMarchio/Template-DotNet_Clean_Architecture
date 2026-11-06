using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class BuildCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeDotnet _dotnet = new();

    [Fact]
    public void Build_PrintsAndRunsTheBuildOfTheSolutionAtTheRoot()
    {
        using var project = SampleSolution.WithSolution();

        Run(["build"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("dotnet build Acme.Shop.slnx --configuration Debug");
        _dotnet.Calls.ShouldBe(["build Acme.Shop.slnx --configuration Debug"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Build_RunsAtTheRootFromASubfolder()
    {
        using var project = SampleSolution.WithSolution();

        Run(["build"], project.Folder("Src")).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe(["build Acme.Shop.slnx --configuration Debug"]);
    }

    [Fact]
    public void Build_RunsTheSlnxWhenTheProjectCarriesOneInstead()
    {
        using var project = new SampleProject(SampleProject.Full);
        File.WriteAllText(Path.Combine(project.Root, "Acme.Shop.slnx"), "<Solution />");

        Run(["build"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _dotnet.Calls.ShouldBe(["build Acme.Shop.slnx --configuration Debug"]);
    }

    [Fact]
    public void Build_ForwardsTheExitCodeOfTheBuild()
    {
        using var project = SampleSolution.WithSolution();
        _dotnet.ExitWith(7);

        Run(["build"], project.Root).ShouldBe(7);
    }

    [Fact]
    public void Build_RefusesOutsideAProject()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["build"], folder).ShouldBe(CleanArchCommand.Failed);
        }
        finally
        {
            Directory.Delete(folder);
        }

        _error.ToString().ShouldStartWith("Not inside a CleanArch project");
        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Build_RefusesAProjectFileItCannotRead()
    {
        using var project = new SampleProject("{ not json");

        Run(["build"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(ProjectLocator.FileName);
        _output.ToString().ShouldBeEmpty();
        _dotnet.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void HelpJson_ListsTheCommandsOfTheProject()
    {
        using var project = SampleSolution.WithSolution();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        JsonArray commands = CommandsOf();
        commands.Select(command => Text(command!["name"])).Where(name => name is "build" or "test" or "coverage").ShouldBe(["build", "test", "coverage"]);
        JsonObject build = commands.Single(command => Text(command!["name"]) == "build").ShouldNotBeNull().AsObject();
        build["options"].ShouldNotBeNull().AsArray().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
        _dotnet.Dispose();
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

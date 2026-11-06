using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class LocalCommandTests : IDisposable
{
    private readonly List<ExternalCall> _calls = [];
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public LocalCommandTests()
    {
        ExternalCommand.Handler = (call, _, _) =>
        {
            _calls.Add(call);
            return CleanArchCommand.Succeeded;
        };
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    [Fact]
    public void ALocalCommand_RunsItsFileBasedApp_WithEveryArgumentAfterItsName()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        AFile(project, "Tools/deploy.cs");

        Run(["deploy", "staging", "--help", "-c", "Release"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        ExternalCall call = _calls.ShouldHaveSingleItem();
        call.FileName.ShouldBe("dotnet");
        call.Arguments.ShouldBe(["run", "Tools/deploy.cs", "--", "staging", "--help", "-c", "Release"]);
        call.WorkingDirectory.ShouldBe(project.Root);
    }

    [Fact]
    public void ALocalCommand_OfABuiltInName_ReplacesTheBuiltInOne()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        AFile(project, "Tools/format.cs");

        Run(["format", "--check"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.ShouldHaveSingleItem().Arguments.ShouldBe(["run", "Tools/format.cs", "--", "--check"]);
    }

    [Fact]
    public void ALocalCommand_ReplacesABuiltInOne_EvenWhereTheBuiltInOneIsRefused()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        AFile(project, "Tools/run.cs");

        Run(["run"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.ShouldHaveSingleItem().Arguments.ShouldBe(["run", "Tools/run.cs"]);
        CommandsOf(project).Select(command => Name(command)).ShouldContain("run");
    }

    [Fact]
    public void HelpJson_SaysWhichCommandsAreLocal_AndWhichReplaceABuiltInOne()
    {
        using var project = new SampleProject(SampleProject.Minimal);
        AFile(project, "Tools/deploy.cs");
        AFile(project, "Tools/format.cs");

        JsonObject[] commands = CommandsOf(project);

        JsonObject deploy = commands.Single(command => Name(command) == "deploy");
        deploy["local"]!.GetValue<string>().ShouldBe("Tools/deploy.cs");
        deploy["replacesBuiltIn"].ShouldBeNull();
        JsonObject format = commands.Single(command => Name(command) == "format");
        format["local"]!.GetValue<string>().ShouldBe("Tools/format.cs");
        format["replacesBuiltIn"]!.GetValue<bool>().ShouldBeTrue();
        format["options"]!.AsArray().ShouldBeEmpty();
        JsonObject build = commands.Single(command => Name(command) == "build");
        build["local"].ShouldBeNull();
        commands.Count(command => Name(command) == "format").ShouldBe(1);
    }

    [Theory]
    [InlineData("Tasks.cs")]
    [InlineData("NewFeature.cs")]
    [InlineData("new_feature.cs")]
    [InlineData("help.cs")]
    public void AFileThatIsNotNamedAsACommand_IsNoCommand(string file)
    {
        using var project = new SampleProject(SampleProject.Minimal);
        AFile(project, "Tools/" + file);

        CommandsOf(project).ShouldAllBe(command => command["local"] == null);
    }

    [Fact]
    public void TheScriptsThisRepositoryKeepsInTools_AreNoCommands()
    {
        Directory.EnumerateFiles(Path.Combine(Repository.Root, "Tools"), "*.cs").ShouldNotBeEmpty();

        Run(["help", "--json"], Repository.Root).ShouldBe(CleanArchCommand.Succeeded);

        CommandsOf().ShouldAllBe(command => command["local"] == null);
    }

    private static void AFile(SampleProject project, string relative)
    {
        project.Folder(Path.GetDirectoryName(relative)!);
        File.WriteAllText(Path.Combine(project.Root, relative), "return 0;\n");
    }

    private static string Name(JsonObject command) => command["name"]!.GetValue<string>();

    private JsonObject[] CommandsOf(SampleProject project)
    {
        _output.GetStringBuilder().Clear();
        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        return CommandsOf();
    }

    private JsonObject[] CommandsOf()
    {
        JsonObject[] commands = [.. JsonNode.Parse(_output.ToString())!["commands"]!.AsArray().OfType<JsonObject>()];
        _output.GetStringBuilder().Clear();
        return commands;
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);
}

using System.CommandLine;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class CommandRegistrationTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private static readonly CommandRegistration _sonar = new(RefuseUnlessSonar, BuildSonar);

    [Fact]
    public void ACommandTheProjectFileMakesTrue_RunsAndIsListed()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: true));

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldContain("SonarCloud");
        _error.ToString().ShouldBeEmpty();
        Clear();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        JsonArray commands = CommandsOf();
        commands.Select(command => Text(command!["name"])).ShouldBe(["help", "sonar"]);
        JsonObject sonar = commands.Single(command => Text(command!["name"]) == "sonar").ShouldNotBeNull().AsObject();
        sonar["options"].ShouldNotBeNull().AsArray().Select(option => Text(option!["name"])).ShouldBe(["--analyse"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ACommandTheProjectFileMakesFalse_IsHiddenAndRefusedWithTheReason()
    {
        using var project = new SampleProject(SampleProject.WithOptions("local", sonar: false));

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain($"its {ProjectLocator.FileName} records sonar false");
        _output.ToString().ShouldBeEmpty();
        Clear();

        Run(["--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldNotContain("sonar");
        Clear();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        CommandsOf().Select(command => Text(command!["name"])).ShouldBe(["help"]);
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void ACommandOutsideAProject_IsRefusedSayingSo()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-cli-").FullName;
        try
        {
            Run(["sonar"], folder).ShouldBe(CleanArchCommand.Failed);
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
    public void ACommandWithAProjectFileItCannotRead_IsRefusedWithThatError()
    {
        using var project = new SampleProject("{ not json");

        Run(["sonar"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        _output.ToString().ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error, [HelpCommand.Registration, _sonar]);

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

    private static string? RefuseUnlessSonar(CommandContext context) =>
        context.Project is not { } project
            ? context.RefusedProjectFile?.Message ?? context.OutsideProject
            : project.File.Options.Sonar == true
                ? null
                : $"This project has no sonar command: its {ProjectLocator.FileName} records sonar false.";

    private static Command BuildSonar(CommandContext context)
    {
        var command = new Command("sonar", "Send this project's code to SonarCloud.");
        if (context.Project is { } project && project.File.Options.Sonar == true)
        {
            command.Options.Add(new Option<bool>("--analyse") { Description = "Analyse the whole solution, not only what changed." });
        }

        command.SetAction(parseResult =>
        {
            parseResult.InvocationConfiguration.Output.WriteLine("SonarCloud analysis of this project.");
            return CleanArchCommand.Succeeded;
        });
        return command;
    }
}

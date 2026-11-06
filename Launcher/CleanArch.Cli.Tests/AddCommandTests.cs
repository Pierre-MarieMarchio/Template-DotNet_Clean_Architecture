using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

[Collection(ProcessWideState.Name)]
public sealed class AddCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly Dictionary<string, int> _exitOf = new(StringComparer.Ordinal);
    private string _installed = string.Empty;

    public AddCommandTests()
    {
        ExternalCommand.Handler = (call, output, _) =>
        {
            _calls.Add(call);
            if (call.Arguments is ["new", "uninstall"])
            {
                output.WriteLine(_installed);
            }

            return _exitOf.FirstOrDefault(exit => string.Join(' ', call.Arguments).Contains(exit.Key, StringComparison.Ordinal)).Value;
        };
    }

    [Fact]
    public void AddWebApp_WhereTheTemplateIsInstalledAtThatVersion_DoesNotInstallItAgain()
    {
        using var project = new SampleProject(SampleProject.Web);
        _installed = "CleanArch.WebApi.Templates\n   Version: 1.2.3\n";

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldNotContain(line => line.Contains("new install", StringComparison.Ordinal));
        Lines().ShouldContain(line => line.Contains("cleanarch-webapp", StringComparison.Ordinal));
    }

    [Fact]
    public void AddWebApp_AnswersItsQuestionsFromTheProjectFile_NotFromItsFiles()
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder(Path.Combine("Src", "Application", "AS.Application.Auth"));

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain("Taken from Acme.Shop:");
        _output.ToString().ShouldContain("--auth local");
        _output.ToString().ShouldContain("--files false");
    }

    [Fact]
    public void AddWebApp_WithDryRun_PrintsTheCommandsItWouldRun_AndRunsAndWritesNothing()
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react", "--dry-run"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.ShouldBeEmpty();
        _output.ToString().ShouldContain("Dry run");
        _output.ToString().ShouldContain("dotnet new install CleanArch.WebApi.Templates@1.2.3");
        _output.ToString().ShouldContain("dotnet new cleanarch-webapp -n Backoffice --framework vite-react");
        _output.ToString().ShouldContain("dotnet run Tools/Tasks.cs web-join Backoffice --web vite-react");
        Directory.EnumerateFileSystemEntries(project.Root).Select(Path.GetFileName).ShouldBe([ProjectLocator.FileName]);
    }

    [Fact]
    public void AddWebApp_WhereTheAppExists_IsRefusedBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder(Path.Combine("Src", "Web", "apps", "Backoffice"));

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("there's already an app with that name");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddWebPackage_WhereTheProjectFileRecordsNoWorkspace_IsRefusedWithTheReason_AndHidden()
    {
        using var project = new SampleProject(SampleProject.Minimal);

        Run(["add", "web-package", "--yes", "--name", "tokens"], project.Root).ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain($"its {ProjectLocator.FileName} records no web workspace");
        _calls.ShouldBeEmpty();
        _output.ToString().ShouldBeEmpty();
        Clear();

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        AddedCommands().ShouldBe(["web-app", "presentation"]);
    }

    [Fact]
    public void AddPresentation_RunsTheTemplate_ThenJoinsAndScaffolds()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldBe(
            [
                "DOTNET_CLI_UI_LANGUAGE=en dotnet new uninstall",
                "DOTNET_CLI_UI_LANGUAGE=en dotnet new install CleanArch.WebApi.Templates@1.2.3",
                "DOTNET_CLI_UI_LANGUAGE=en dotnet new cleanarch-presentation -n Portal --framework blazor-server --project-name Acme.Shop --examples none --files false --agent-files false --has-client true --ci false",
                "dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Portal",
                "dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal",
            ]);
        _output.ToString().ShouldContain("Scaffold done.");
    }

    [Fact]
    public void AddPresentation_WhenTheJoinFails_SkipsTheScaffold()
    {
        using var project = new SampleProject(SampleProject.Api);
        _exitOf["presentation-join"] = 1;

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldNotContain(line => line.Contains("presentation-scaffold", StringComparison.Ordinal) && !line.StartsWith("  ", StringComparison.Ordinal));
        _output.ToString().ShouldContain("Scaffold skipped.");
    }

    public void Dispose()
    {
        ExternalCommand.Handler = null;
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        CleanArchCommand.Run(arguments, workingDirectory, _output, _error);

    private string[] Lines() => [.. _calls.Select(call => ExternalCommand.LineOf(call))];

    private void Clear()
    {
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
    }

    private string[] AddedCommands() =>
        [.. Command(JsonNode.Parse(_output.ToString()).ShouldNotBeNull().AsObject(), "add")["commands"].ShouldNotBeNull().AsArray().Select(command => Text(command!["name"]))];

    private static JsonObject Command(JsonObject parent, string name) =>
        parent["commands"].ShouldNotBeNull().AsArray().Single(command => Text(command!["name"]) == name).ShouldNotBeNull().AsObject();

    private static string Text(JsonNode? node) => node.ShouldNotBeNull().GetValue<string>();
}

using System.Text.Json.Nodes;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class AddCommandTests : IDisposable
{
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];
    private readonly Dictionary<string, int> _exitOf = new(StringComparer.Ordinal);
    private readonly FakeProcesses _processes;
    private string _installed = string.Empty;

    public AddCommandTests()
    {
        _processes = new FakeProcesses((call, output, _) =>
        {
            _calls.Add(call);
            WriteTheItem(call);
            if (call.Arguments is ["new", "uninstall"])
            {
                output.WriteLine(_installed);
            }

            return _exitOf.FirstOrDefault(exit => string.Join(' ', call.Arguments).Contains(exit.Key, StringComparison.Ordinal)).Value;
        });
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

    [Theory]
    [InlineData("presentation", "Portal", "blazor-server", "Src/Presentation/AS.Portal", "dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Portal", "dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal")]
    [InlineData("web-app", "Backoffice", "vite-react", "Src/Web/apps/Backoffice", "dotnet run Tools/Tasks.cs web-join Backoffice --web vite-react", "dotnet run Tools/Tasks.cs web-scaffold Backoffice")]
    public void AnAddition_WhoseFolderExists_CompletesIt_WithoutGeneratingItAgain(string addition, string name, string framework, string folder, string join, string scaffold)
    {
        using var project = new SampleProject(SampleProject.ApiWithWeb);
        File.WriteAllText(Path.Combine(project.Folder(folder), "scaffold.json"), "{}");

        Run(["add", addition, "--yes", "--name", name, "--framework", framework], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldBe([join, scaffold]);
    }

    [Fact]
    public void AnAddition_WhoseJoinIsDone_RunsOnlyTheScaffold()
    {
        using var project = new SampleProject(SampleProject.Web);
        File.WriteAllText(Path.Combine(project.Folder("Src/Web/apps/Site"), "scaffold.json"), "{}");

        Run(["add", "web-app", "--name", "Site"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldBe(["dotnet run Tools/Tasks.cs web-scaffold Site"]);
    }

    [Theory]
    [InlineData("web-app", "Site", "Src/Web/apps/Site is joined and created already")]
    [InlineData("web-package", "ui", "Src/Web/packages/ui is recorded in cleanarch.json already")]
    public void AnAddition_WithNothingLeftToComplete_IsRefused_AndRunsNothing(string addition, string name, string refusal)
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder("Src/Web/apps/Site");
        project.Folder("Src/Web/packages/ui");

        Run(["add", addition, "--name", name], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(refusal);
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
        AddedCommands().ShouldNotContain("web-package");
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
    public void AddWebApp_RunsTheTemplate_ThenJoinsAndScaffolds()
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines()[^2..].ShouldBe(
            [
                "dotnet run Tools/Tasks.cs web-join Backoffice --web vite-react --web-audience public --web-rendering spa",
                "dotnet run Tools/Tasks.cs web-scaffold Backoffice",
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

    [Fact]
    public void AddWebApp_WhereTheProjectHoldsABlazorWebAssemblyApp_IsRefusedBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder(Path.Combine("Src", "Web", "apps", "Wasm"));
        File.WriteAllText(Path.Combine(project.Root, "Src", "Web", "apps", "Wasm", "Acme.Shop.Wasm.csproj"), string.Empty);

        Run(["add", "web-app", "--yes", "--name", "Second", "--framework", "blazor-wasm"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("Acme.Shop already has a Blazor WebAssembly app, Src/Web/apps/Wasm. This version adds only one per project.");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddWebApp_GivenAPort_PassesItToTheJoin()
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", "web-app", "--yes", "--name", "Wasm", "--framework", "vite-react", "--port", "8085"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldContain("dotnet run Tools/Tasks.cs web-join Wasm --web vite-react --web-audience public --web-rendering spa --web-port 8085");
    }

    [Fact]
    public void AddWebApp_WhenTheJoinFails_LeavesTheJoiningSectionByHand()
    {
        using var project = new SampleProject(SampleProject.Web);
        _exitOf["web-join"] = 1;

        Run(["add", "web-app", "--yes", "--name", "Wasm", "--framework", "vite-react"], project.Root);

        _output.ToString().ShouldContain("Join failed.");
        _output.ToString().ShouldContain("dotnet run Tools/Tasks.cs web-join Wasm --web vite-react --web-audience public --web-rendering spa");
        _output.ToString().ShouldContain("To do by hand, from");
        _output.ToString().ShouldContain("Src/Web/apps/Wasm/README.md");
    }

    [Theory]
    [InlineData("Api", "names a project the template already writes")]
    [InlineData("portal", "use PascalCase, like Portal.")]
    public void AddPresentation_WithANameTheProjectCannotTake_IsRefusedBeforeRunningAnything(string name, string refusal)
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(Path.Combine("Src", "Presentation", "AS.Portal"));

        Run(["add", "presentation", "--yes", "--name", name, "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(refusal);
        _calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("w", "--project-name takes two characters or more")]
    [InlineData("_1", "--project-name starts with a letter from A to Z")]
    public void AddPresentation_InAProjectWhoseNameTheTemplateRefuses_IsRefusedBeforeRunningAnything(string projectName, string rule)
    {
        using var project = new SampleProject(SampleProject.Named(projectName));

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain($"refuses {projectName}, whatever the answers: {rule}");
        _error.ToString().ShouldContain("Nothing was written");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddPresentation_WhenThePagesFail_SaysWhatToRunAgain()
    {
        using var project = new SampleProject(SampleProject.Api);
        _exitOf["presentation-scaffold"] = 1;

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root);

        _output.ToString().ShouldContain("Scaffold failed.");
        _output.ToString().ShouldContain("dotnet run Tools/Tasks.cs presentation-scaffold Acme.Shop.Portal");
    }

    [Fact]
    public void AddPresentation_WithoutAName_ProposesTheFrameworksName()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["add", "presentation", "--yes", "--framework", "worker"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldContain(line => line.Contains("cleanarch-presentation -n Worker --framework worker ", StringComparison.Ordinal));
        Lines().ShouldContain("dotnet run Tools/Tasks.cs presentation-join Acme.Shop.Worker");
    }

    [Fact]
    public void AddPresentation_OfTheWorker_UnderAnotherName_IsRefusedBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["add", "presentation", "--yes", "--framework", "worker", "--name", "Jobs"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(RefusalOf("workerIsNamedWorker"));
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddPresentation_OfAClientOfTheApi_InAProjectWithoutTheApi_IsRefusedBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.NoApi);

        Run(["add", "presentation", "--yes", "--framework", "avalonia"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(RefusalOf("presentationNeedsTheApi"));
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddPresentation_OfTheMcpServer_ListsTheIdentityMigration_OnlyWhereTheAccessKeysAreAdded()
    {
        using var project = new SampleProject(SampleProject.ApiWith("local", client: false));

        Run(["add", "presentation", "--yes", "--framework", "mcp"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldContain("migration-add AddAccessKeys --context auth");
        Clear();

        Run(["add", "presentation", "--yes", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldNotContain("migration-add");
    }

    [Fact]
    public void ThePartsOfTheReadmesAnAdditionPrints_AreInTheTemplate()
    {
        MarkdownSection.Of(Path.Combine(Repository.Root, "Src", "Web", "README.md"), ItemAdditions.JoiningSection)
            .ShouldNotBeNull().ShouldContain(ItemAdditions.WebJoinTask);
        MarkdownSection.Of(Path.Combine(Repository.Root, "Src", "Web", "apps", "FrameworkSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        MarkdownSection.Of(Path.Combine(Repository.Root, "Src", "Web", "apps", "PlaceholderSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        MarkdownSection.Of(Path.Combine(Repository.Root, ".template.config", "variants", "project", "README.md"), "## Run it")
            .ShouldNotBeNullOrWhiteSpace();
        string presentation = Path.Combine(Repository.Root, ".template.config", "starters", "blazor-server", "Src", "Presentation", "AT.PresentationAppPortal", "README.md");
        MarkdownSection.Of(presentation, ItemAdditions.JoiningSection).ShouldNotBeNull().ShouldContain(ItemAdditions.PresentationJoinTask);
        MarkdownSection.Of(presentation, ItemAdditions.PresentationCreateSection).ShouldNotBeNull().ShouldContain(ItemAdditions.PresentationScaffoldTask);
        MarkdownSection.Of(Path.Combine(Repository.Root, ".template.config", "starters", "blazor-wasm", "FrameworkSite", "README.md"), ItemAdditions.JoiningSection)
            .ShouldNotBeNull().ShouldContain(ItemAdditions.WebJoinTask);
    }

    [Fact]
    public void TheTasksAnAdditionRuns_AreTasksOfTheProject()
    {
        string tasks = File.ReadAllText(Path.Combine(Repository.Root, "Tools", "Tasks.cs"));

        tasks.ShouldContain($"case \"{ItemAdditions.PresentationJoinTask}\":");
        tasks.ShouldContain($"case \"{ItemAdditions.PresentationScaffoldTask}\":");
        tasks.ShouldContain($"case \"{ItemAdditions.WebJoinTask}\":");
    }

    [Theory]
    [InlineData("web-app", "Framework: placeholder|angular", "--has-client")]
    [InlineData("web-package", "--scope", "--name")]
    [InlineData("presentation", "Framework: blazor-server|avalonia|mcp|wpf|worker", "--has-api")]
    public void TheHelpOfAnAddition_ShowsThatTemplatesOptions_NotTheProjects(string addition, string shown, string alsoShown)
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", addition, "--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _output.ToString().ShouldContain(shown);
        _output.ToString().ShouldContain(alsoShown);
        _output.ToString().ShouldNotContain("--presentation api|");
    }

    private static string RefusalOf(string symbol) =>
        TemplateSource.FromPath(Repository.Root).Manifest(TemplateKind.Presentation).Refusals.Single(refusal => refusal.Symbol == symbol).Explanation;

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _processes);

    private string[] Lines() => [.. _calls.Select(call => call.Line)];

    private static void WriteTheItem(ExternalCall call)
    {
        int named = call.Arguments.ToList().IndexOf("-n");
        string? folder = named < 1 ? null : call.Arguments[named - 1] switch
        {
            "cleanarch-presentation" => Path.Combine("Src", "Presentation", "AS." + call.Arguments[named + 1]),
            "cleanarch-webapp" => Path.Combine("Src", "Web", "apps", call.Arguments[named + 1]),
            _ => null,
        };
        if (folder is not null)
        {
            string written = Directory.CreateDirectory(Path.Combine(call.WorkingDirectory, folder)).FullName;
            File.WriteAllText(Path.Combine(written, "scaffold.json"), "{}");
        }
    }

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

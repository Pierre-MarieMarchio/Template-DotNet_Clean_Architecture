using System.Text.Json.Nodes;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Processes;
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
    private readonly FakeSteps _steps = new();
    private string _installed = string.Empty;

    public AddCommandTests()
    {
        _processes = new FakeProcesses((call, output, _) =>
        {
            _calls.Add(call);
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
        project.Folder(Path.Combine("src", "Application", "AS.Application.Auth"));

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
        _output.ToString().ShouldContain("Join: Making the edits its README lists");
        _output.ToString().ShouldContain("Scaffold: Creating it with its framework's official CLI");
        _steps.Ran.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(project.Root).Select(Path.GetFileName).ShouldBe([ProjectLocator.FileName]);
    }

    [Theory]
    [InlineData("presentation", "Portal", "blazor-server", "src/Presentation/AS.Portal", "presentation-join Acme.Shop.Portal", "presentation-scaffold Acme.Shop.Portal")]
    [InlineData("web-app", "Backoffice", "vite-react", "src/Web/apps/Backoffice", "web-join Backoffice --framework vite-react", "web-scaffold Backoffice")]
    public void AnAddition_WhoseFolderExists_CompletesIt_WithoutGeneratingItAgain(string addition, string name, string framework, string folder, string join, string scaffold)
    {
        using var project = new SampleProject(SampleProject.ApiWithWeb);
        project.Folder(folder);

        Run(["add", addition, "--yes", "--name", name, "--framework", framework], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _calls.ShouldBeEmpty();
        _steps.Ran.ShouldBe([join, scaffold]);
    }

    [Fact]
    public void AnAddition_WhoseJoinIsDone_RunsOnlyTheScaffold()
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder("src/Web/apps/Site");
        _steps.Done.Add("web-join Site");

        Run(["add", "web-app", "--name", "Site"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _steps.Ran.ShouldBe(["web-scaffold Site"]);
    }

    [Theory]
    [InlineData("web-app", "Site", "src/Web/apps/Site is joined and created already", "web-join Site", "web-scaffold Site")]
    [InlineData("web-package", "ui", "src/Web/packages/ui is recorded in cleanarch.json already", "web-join ui")]
    public void AnAddition_WithNothingLeftToComplete_IsRefused_AndRunsNothing(string addition, string name, string refusal, params string[] done)
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder("src/Web/apps/Site");
        project.Folder("src/Web/packages/ui");
        _steps.Done.UnionWith(done);

        Run(["add", addition, "--name", name], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(refusal);
        _calls.ShouldBeEmpty();
        _steps.Ran.ShouldBeEmpty();
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
                "DOTNET_CLI_UI_LANGUAGE=en dotnet new cleanarch-presentation -n Portal --framework blazor-server --project-name Acme.Shop --examples none --files false --has-client true --ci false",
            ]);
        _steps.Ran.ShouldBe(["presentation-join Acme.Shop.Portal", "presentation-scaffold Acme.Shop.Portal"]);
        _output.ToString().ShouldContain("Scaffold done.");
    }

    [Fact]
    public void AddWebApp_RunsTheTemplate_ThenJoinsAndScaffolds()
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", "web-app", "--yes", "--name", "Backoffice", "--framework", "vite-react"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _steps.Ran.ShouldBe(["web-join Backoffice --framework vite-react --audience public --rendering spa", "web-scaffold Backoffice"]);
        _output.ToString().ShouldContain("Scaffold done.");
    }

    [Fact]
    public void AddPresentation_WhenTheJoinFails_SkipsTheScaffold()
    {
        using var project = new SampleProject(SampleProject.Api);
        _steps.Failing.Add("presentation-join Acme.Shop.Portal");

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _steps.Ran.ShouldBe(["presentation-join Acme.Shop.Portal"]);
        _output.ToString().ShouldContain("Scaffold skipped.");
    }

    [Fact]
    public void AddWebApp_WhereTheProjectHoldsABlazorWebAssemblyApp_IsRefusedBeforeRunningAnything()
    {
        using var project = new SampleProject(SampleProject.Web);
        project.Folder(Path.Combine("src", "Web", "apps", "Wasm"));
        File.WriteAllText(Path.Combine(project.Root, "src", "Web", "apps", "Wasm", "Acme.Shop.Wasm.csproj"), string.Empty);

        Run(["add", "web-app", "--yes", "--name", "Second", "--framework", "blazor-wasm"], project.Root).ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain("Acme.Shop already has a Blazor WebAssembly app, src/Web/apps/Wasm. This version adds only one per project.");
        _calls.ShouldBeEmpty();
    }

    [Fact]
    public void AddWebApp_GivenAPort_PassesItToTheJoin()
    {
        using var project = new SampleProject(SampleProject.Web);

        Run(["add", "web-app", "--yes", "--name", "Wasm", "--framework", "vite-react", "--port", "8085"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        _steps.Ran.ShouldContain("web-join Wasm --framework vite-react --audience public --rendering spa --port 8085");
    }

    [Fact]
    public void AddWebApp_WhenTheJoinFails_LeavesTheJoiningSectionByHand()
    {
        using var project = new SampleProject(SampleProject.Web);
        _steps.Failing.Add("web-join Wasm --framework vite-react --audience public --rendering spa");

        Run(["add", "web-app", "--yes", "--name", "Wasm", "--framework", "vite-react"], project.Root);

        _output.ToString().ShouldContain("Join failed.");
        _output.ToString().ShouldContain("dotnet cleanarch add web-app --name Wasm");
        _output.ToString().ShouldContain("To do by hand, from");
        _output.ToString().ShouldContain("src/Web/apps/Wasm/README.md");
    }

    [Theory]
    [InlineData("Api", "names a project the template already writes")]
    [InlineData("portal", "use PascalCase, like Portal.")]
    public void AddPresentation_WithANameTheProjectCannotTake_IsRefusedBeforeRunningAnything(string name, string refusal)
    {
        using var project = new SampleProject(SampleProject.Api);
        project.Folder(Path.Combine("src", "Presentation", "AS.Portal"));

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
        _steps.Failing.Add("presentation-scaffold Acme.Shop.Portal");

        Run(["add", "presentation", "--yes", "--name", "Portal", "--framework", "blazor-server"], project.Root);

        _output.ToString().ShouldContain("Scaffold failed.");
        _output.ToString().ShouldContain("dotnet cleanarch add presentation --name Portal");
    }

    [Fact]
    public void AddPresentation_WithoutAName_ProposesTheFrameworksName()
    {
        using var project = new SampleProject(SampleProject.Api);

        Run(["add", "presentation", "--yes", "--framework", "worker"], project.Root).ShouldBe(CleanArchCommand.Succeeded);

        Lines().ShouldContain(line => line.Contains("cleanarch-presentation -n Worker --framework worker ", StringComparison.Ordinal));
        _steps.Ran.ShouldContain("presentation-join Acme.Shop.Worker");
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
        _output.ToString().ShouldContain("dotnet cleanarch migrations add AddAccessKeys --context auth");
        Clear();

        Run(["add", "presentation", "--yes", "--framework", "blazor-server"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        _output.ToString().ShouldNotContain("migrations add");
    }

    [Theory]
    [InlineData("presentation", "presentation", 1)]
    [InlineData("webapp", "web-app", 2)]
    [InlineData("webpackage", "web-package", 1)]
    public void TheItemTemplatesInstructions_NameTheAdditionThatRunsTheirSteps_SoItLeavesThemOut(string folder, string item, int steps)
    {
        JsonNode template = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, ".template.config", folder, ".template.config", "template.json")))!;

        template["postActions"]!.AsArray()
            .Select(action => action!["manualInstructions"]![0]!["text"]!.GetValue<string>())
            .Count(text => text.Contains($"dotnet cleanarch add {item} --name <name>", StringComparison.Ordinal))
            .ShouldBe(steps, $"The instructions of cleanarch-{folder} name the steps dotnet cleanarch add {item} runs in another form, so it relays them as left to do.");
    }

    [Fact]
    public void ThePartsOfTheReadmesAnAdditionPrints_AreInTheTemplate()
    {
        MarkdownSection.Of(Path.Combine(Repository.Root, "src", "Web", "README.md"), ItemAdditions.JoiningSection)
            .ShouldNotBeNull().ShouldContain("dotnet cleanarch add web-app --name");
        MarkdownSection.Of(Path.Combine(Repository.Root, "src", "Web", "apps", "FrameworkSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        MarkdownSection.Of(Path.Combine(Repository.Root, "src", "Web", "apps", "PlaceholderSite", "README.md"), "## Join the stack")
            .ShouldNotBeNullOrWhiteSpace();
        MarkdownSection.Of(Path.Combine(Repository.Root, ".template.config", "variants", "project", "README.md"), "## Run it")
            .ShouldNotBeNullOrWhiteSpace();
        string presentation = Path.Combine(Repository.Root, ".template.config", "starters", "blazor-server", "src", "Presentation", "AT.PresentationAppPortal", "README.md");
        MarkdownSection.Of(presentation, ItemAdditions.JoiningSection).ShouldNotBeNull().ShouldContain("dotnet cleanarch add presentation --name PresentationAppPortal");
        MarkdownSection.Of(presentation, ItemAdditions.PresentationCreateSection).ShouldNotBeNull().ShouldContain("dotnet cleanarch add presentation --name PresentationAppPortal");
        MarkdownSection.Of(Path.Combine(Repository.Root, ".template.config", "starters", "blazor-wasm", "FrameworkSite", "README.md"), ItemAdditions.JoiningSection)
            .ShouldNotBeNull().ShouldContain("dotnet cleanarch add web-app --name FrameworkSite");
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
        CleanArchCommand.Run(
            arguments,
            workingDirectory,
            _output,
            _error,
            [.. BuiltInCommands.All.Where(registration => registration != AddCommand.Registration), AddCommand.Using(_ => _steps)],
            _processes);

    private string[] Lines() => [.. _calls.Select(call => call.Line)];

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

    private sealed class FakeSteps : IItemSteps
    {
        public List<string> Ran { get; } = [];

        public HashSet<string> Done { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);

        public ItemStep WebJoin(string root, string name, WebJoinOptions options) =>
            Step(string.Join(' ', new[]
            {
                "web-join", name,
                Given("--framework", options.Framework), Given("--audience", options.Audience), Given("--rendering", options.Rendering), Given("--port", options.Port),
            }.Where(part => part.Length > 0)));

        public ItemStep WebScaffold(string root, string name) => Step($"web-scaffold {name}");

        public ItemStep PresentationJoin(string root, string project) => Step($"presentation-join {project}");

        public ItemStep PresentationScaffold(string root, string project) => Step($"presentation-scaffold {project}");

        private static string Given(string option, string? value) => value is null ? string.Empty : $"{option} {value}";

        private ItemStep Step(string step) =>
            new(() => Done.Contains(step), _ =>
            {
                Ran.Add(step);
                return new ProcessResult(Failing.Contains(step) ? CleanArchCommand.Failed : CleanArchCommand.Succeeded, string.Empty);
            });
    }
}

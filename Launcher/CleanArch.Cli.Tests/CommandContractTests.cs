using System.CommandLine;
using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class CommandContractTests : IDisposable
{
    private static readonly Dictionary<string, string> _projectFiles = new(StringComparer.Ordinal)
    {
        ["auth none, no presentation"] = SampleProject.Minimal,
        ["auth local, blazor presentation, sonar false, client, web"] = SampleProject.Full,
        ["auth local, api, sonar true, client"] = SampleProject.Api,
        ["auth local, api, sonar true, web"] = SampleProject.ApiWithWeb,
        ["auth external, api, no client"] = SampleProject.ApiWith("external", client: false),
    };

    private static readonly string[] _runOutsideAProject = [HelpCommand.Name, AdoptCommand.Name];

    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly List<ExternalCall> _calls = [];

    private readonly FakeProcesses _processes;

    public CommandContractTests() =>
        _processes = new FakeProcesses((call, _, _) =>
        {
            _calls.Add(call);
            return CleanArchCommand.Succeeded;
        });

    public static TheoryData<string> ProjectFiles => [.. _projectFiles.Keys];

    [Fact]
    public void OutsideAProject_EveryCommandButHelpAndAdopt_IsHiddenAndRefusedWithTheReason_AndRunsNothing()
    {
        string folder = Directory.CreateTempSubdirectory("cleanarch-contract-").FullName;
        try
        {
            CommandContext context = CommandContext.Locate(folder, _processes);
            foreach (string[] path in BuiltIns(context).Where(command => !_runOutsideAProject.Contains(command.Name)).SelectMany(command => PathsOf(command, [])))
            {
                Clear();
                Run(path, folder).ShouldBe(CleanArchCommand.Failed, string.Join(' ', path));
                _error.ToString().ShouldBe(context.OutsideProject + Environment.NewLine, string.Join(' ', path));
                _output.ToString().ShouldBeEmpty(string.Join(' ', path));
            }

            Clear();
            Run(["--help"], folder).ShouldBe(CleanArchCommand.Succeeded);
            ListedIn(_output.ToString()).ShouldBe(_runOutsideAProject, ignoreOrder: true);
            _calls.ShouldBeEmpty();
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void WithAProjectFileItCannotRead_EveryCommandButHelp_IsHiddenAndRefusedWithThatError_AndRunsNothing()
    {
        using var project = new SampleProject("{ not json");
        CommandContext context = CommandContext.Locate(project.Root, _processes);
        string unreadable = context.RefusedProjectFile.ShouldNotBeNull().Message;
        unreadable.ShouldContain(Path.Combine(project.Root, ProjectLocator.FileName));
        Dictionary<string, string> before = Snapshot(project.Root);

        foreach (string[] path in BuiltIns(context).Where(command => command.Name != HelpCommand.Name).SelectMany(command => PathsOf(command, [])))
        {
            Clear();
            Run(path, project.Root).ShouldBe(CleanArchCommand.Failed, string.Join(' ', path));
            _error.ToString().ShouldStartWith(unreadable, customMessage: string.Join(' ', path));
            _output.ToString().ShouldBeEmpty(string.Join(' ', path));
        }

        Clear();
        Run(["--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        ListedIn(_output.ToString()).ShouldBe([HelpCommand.Name]);
        _calls.ShouldBeEmpty();
        Snapshot(project.Root).ShouldBe(before);
    }

    [Theory]
    [MemberData(nameof(ProjectFiles))]
    public void ACommandTheProjectFileRefuses_IsHiddenAndRefusedWithItsReason_AndRunsNothing_WhileTheOthersAreListed(string projectFile)
    {
        using var project = new SampleProject(_projectFiles[projectFile]);
        CommandContext context = CommandContext.Locate(project.Root, _processes);
        (string Name, string? Refusal)[] commands = [.. BuiltInCommands.All.Select(registration => (registration.Build(context).Name, registration.Refusal(context)))];
        string[] applying = [.. commands.Where(command => command.Refusal is null).Select(command => command.Name)];
        Dictionary<string, string> before = Snapshot(project.Root);

        Run(["help", "--json"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        JsonNode.Parse(_output.ToString()).ShouldNotBeNull()["commands"].ShouldNotBeNull().AsArray()
            .Select(command => command!["name"]!.GetValue<string>()).ShouldBe(applying);
        Clear();
        Run(["help"], project.Root).ShouldBe(CleanArchCommand.Succeeded);
        ListedIn(_output.ToString()).ShouldBe(applying);

        foreach ((string name, string? refusal) in commands.Where(command => command.Refusal is not null))
        {
            foreach (string[] invocation in new[] { [name], new[] { name, "--help" } })
            {
                Clear();
                Run(invocation, project.Root).ShouldBe(CleanArchCommand.Failed, string.Join(' ', invocation));
                _error.ToString().ShouldBe(refusal + Environment.NewLine, string.Join(' ', invocation));
                _output.ToString().ShouldBeEmpty(string.Join(' ', invocation));
            }
        }

        _calls.ShouldBeEmpty();
        Snapshot(project.Root).ShouldBe(before);
    }

    [Theory]
    [InlineData("auth none, no presentation", "openapi run end-to-end sonar adopt")]
    [InlineData("auth local, blazor presentation, sonar false, client, web", "openapi run end-to-end sonar adopt")]
    [InlineData("auth local, api, sonar true, client", "adopt")]
    [InlineData("auth local, api, sonar true, web", "adopt")]
    [InlineData("auth external, api, no client", "sonar adopt")]
    public void EachRefusalCondition_HoldsAgainstTheProjectFiles(string projectFile, string refused)
    {
        using var project = new SampleProject(_projectFiles[projectFile]);
        CommandContext context = CommandContext.Locate(project.Root, _processes);

        BuiltInCommands.All.Where(registration => registration.Refusal(context) is not null).Select(registration => registration.Build(context).Name)
            .ShouldBe(refused.Split(' '), ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(ProjectFiles))]
    public void TheHelpOfEveryCommandThatApplies_ReadsDotnetCleanarch(string projectFile)
    {
        using var project = new SampleProject(_projectFiles[projectFile]);
        CommandContext context = CommandContext.Locate(project.Root, _processes);
        IEnumerable<Command> applying = BuiltInCommands.All.Where(registration => registration.Refusal(context) is null).Select(registration => registration.Build(context));

        foreach (string[] path in applying.SelectMany(command => ListedPathsOf(command, [])).Prepend([]))
        {
            Clear();
            Run([.. path, "--help"], project.Root).ShouldBe(CleanArchCommand.Succeeded, string.Join(' ', path));
            _output.ToString().ShouldContain($"  {string.Join(' ', [CleanArchCommand.Name, .. path])} ", customMessage: string.Join(' ', path));
        }

        _calls.ShouldBeEmpty();
    }

    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(string[] arguments, string workingDirectory) =>
        BuiltInCommands.Run(arguments, workingDirectory, _output, _error, _processes);

    private void Clear()
    {
        _output.GetStringBuilder().Clear();
        _error.GetStringBuilder().Clear();
    }

    private static IEnumerable<Command> BuiltIns(CommandContext context) =>
        BuiltInCommands.All.Select(registration => registration.Build(context));

    private static IEnumerable<string[]> PathsOf(Command command, string[] parent)
    {
        string[] path = [.. parent, command.Name];
        return command.Subcommands.SelectMany(subcommand => PathsOf(subcommand, path)).Prepend(path);
    }

    private static IEnumerable<string[]> ListedPathsOf(Command command, string[] parent)
    {
        string[] path = [.. parent, command.Name];
        return command.Subcommands.Where(subcommand => !subcommand.Hidden).SelectMany(subcommand => ListedPathsOf(subcommand, path)).Prepend(path);
    }

    private static string[] ListedIn(string help) =>
        [.. help.ReplaceLineEndings("\n").Split('\n')
            .SkipWhile(line => line != "Commands:")
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && line[2] != ' ')
            .Select(line => line.Trim().Split(' ')[0])];

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(root, path), path => File.Exists(path) ? File.ReadAllText(path) : "/");
}

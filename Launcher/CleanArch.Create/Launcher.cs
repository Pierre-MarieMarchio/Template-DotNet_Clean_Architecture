using System.Text.RegularExpressions;
using CleanArch.Create.Manifests;
using CleanArch.Create.Projects;
using CleanArch.Create.Prompts;
using Spectre.Console;

namespace CleanArch.Create;

public sealed partial class Launcher
{
    public const string Invocation = "dnx CleanArch.Create";

    public static readonly IReadOnlyDictionary<string, Condition> ProjectAskedWhen = new Dictionary<string, Condition>(StringComparer.Ordinal)
    {
        ["webName"] = Condition.Parse("(web != \"none\")"),
        ["webAudience"] = Condition.Parse("(web != \"none\")"),
    };

    private const string _help =
        """
        Usage: dnx CleanArch.Create [new|web-app|web-package] [options] [--<template option> <value>]...

        Commands
          new           create a project from cleanarch-webapi (the default outside a project)
          web-app       add a web app to the project around the current folder (cleanarch-webapp)
          web-package   add a package to its web workspace (cleanarch-webpackage)

        Options
          -n, --name <name>            the project, app or package name
          -o, --output <folder>        where new writes the project (default: ./<name>)
          -y, --yes                    take the default of every option not given
          --template-source <path>     install the templates from a checkout or a .nupkg instead of nuget.org
          --hive <folder>              the template engine's settings folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help

        Every option of the template is a flag of the same name, e.g. --auth none --examples none.
        Without a terminal, every answer is a flag, or --yes takes the defaults.
        """;

    private static readonly IReadOnlyDictionary<string, string> _nothing = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, Condition> _alwaysAsked = new Dictionary<string, Condition>();

    private readonly IAnsiConsole _console;
    private readonly string _workingDirectory;
    private readonly bool _interactive;

    public Launcher(IAnsiConsole console, string workingDirectory, bool interactive)
    {
        _console = console;
        _workingDirectory = workingDirectory;
        _interactive = interactive;
    }

    public int Run(IReadOnlyList<string> arguments)
    {
        try
        {
            Arguments parsed = Arguments.Parse(arguments);
            if (parsed.Help)
            {
                _console.Verbatim(_help);
                return 0;
            }

            TemplateSource source = parsed.TemplateSource is { } path
                ? TemplateSource.FromPath(Path.GetFullPath(path, _workingDirectory))
                : TemplateSource.Embedded();
            var questionnaire = new Questionnaire(_console, _interactive, parsed.Yes);
            var dotnet = new TemplateEngine(_console, parsed.Hive is { } hive ? Path.GetFullPath(hive, _workingDirectory) : null);
            GeneratedProject? project = GeneratedProject.Find(_workingDirectory);

            switch (parsed.Command ?? Choose(project, questionnaire))
            {
                case Arguments.WebAppCommand:
                    WebApp(parsed, source, questionnaire, dotnet, Required(project, Arguments.WebAppCommand));
                    break;
                case Arguments.WebPackageCommand:
                    WebPackage(parsed, source, questionnaire, dotnet, Required(project, Arguments.WebPackageCommand));
                    break;
                default:
                    New(parsed, source, questionnaire, dotnet);
                    break;
            }

            return 0;
        }
        catch (LauncherException error)
        {
            _console.MarkupLine("[red]" + Markup.Escape(error.Message) + "[/]");
            return 1;
        }
    }

    internal static string? MarkdownSection(string path, string heading)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string[] lines = File.ReadAllLines(path);
        int start = Array.IndexOf(lines, heading);
        if (start < 0)
        {
            return null;
        }

        int level = HeadingDepth(heading);
        var body = new List<string>();
        bool fenced = false;
        foreach (string line in lines.Skip(start + 1))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && HeadingDepth(line) is > 0 and int depth && depth <= level)
            {
                break;
            }

            body.Add(line);
        }

        return string.Join(Environment.NewLine, body).Trim();
    }

    private static int HeadingDepth(string line)
    {
        int depth = line.Length - line.TrimStart('#').Length;
        return depth > 0 && line.Length > depth && line[depth] == ' ' ? depth : 0;
    }

    private static GeneratedProject Required(GeneratedProject? project, string command) =>
        project ?? throw new LauncherException(
            $"{command} runs inside a project generated by cleanarch-webapi, and no folder from here up holds one solution beside Tools/Tasks.cs.");

    private static string Version(TemplateSource source) =>
        source.Manifest(TemplateKind.Project).Version
            ?? throw new LauncherException("The template source declares no templateVersion, so no package version can be installed.");

    private static string? ProjectNameProblem(string name) =>
        ProjectName().IsMatch(name) ? null : "a project name is dot-separated C# identifiers, as Company.Product.";

    private static string? AppNameProblem(string name) =>
        AppName().IsMatch(name) ? null : "an app name is PascalCase, as BackOffice: it names the app's folder, service and port variable.";

    private static string? PackageNameProblem(string name) =>
        PackageName().IsMatch(name) ? null : "a package name is letters, digits and dashes, starting with a letter, as tokens.";

    private string Choose(GeneratedProject? project, Questionnaire questionnaire)
    {
        if (project is null || !questionnaire.Asks)
        {
            return Arguments.NewCommand;
        }

        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Arguments.WebAppCommand] = $"add a web app to {project.Name}",
            [Arguments.WebPackageCommand] = $"add a package to {project.Name}'s web workspace",
            [Arguments.NewCommand] = "create another project",
        };

        return _console.Prompt(new SelectionPrompt<string>()
            .Title($"[bold]{Markup.Escape(project.Name)}[/] is a generated project. What next?")
            .AddChoices(project.HasWorkspace ? descriptions.Keys : descriptions.Keys.Where(command => command != Arguments.WebPackageCommand))
            .UseConverter(command => $"{command} [grey]{Markup.Escape(descriptions[command])}[/]"));
    }

    private void New(Arguments parsed, TemplateSource source, Questionnaire questionnaire, TemplateEngine dotnet)
    {
        TemplateManifest manifest = source.Manifest(TemplateKind.Project);
        string? name = questionnaire.Text("--name", "Project name", parsed.Name, manifest.DefaultName, ProjectNameProblem);
        string? output = parsed.Output
            ?? (questionnaire.Asks && name is not null
                ? questionnaire.Text("--output", "Output folder", null, Path.Combine(".", name), _ => null)
                : name);
        Dictionary<string, string> answers = questionnaire.Answer(manifest, parsed.TemplateOptions, _nothing, ProjectAskedWhen);

        string target = Path.GetFullPath(output!, _workingDirectory);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new LauncherException($"'{target}' is not empty: choose another --output.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(answers);
        dotnet.EnsureInstalled(source, Version(source), _workingDirectory);
        dotnet.Run(_workingDirectory, [manifest.ShortName, "-n", name!, "-o", target, .. options]);

        Equivalents(parsed, Arguments.NewCommand, [manifest.ShortName, "-n", name!, "-o", output!, .. options]);
        _console.MarkupLine("[bold]Next[/]");
        _console.Verbatim(TemplateEngine.CommandLine("cd", [output!]));
        _console.Verbatim(MarkdownSection(Path.Combine(target, "README.md"), "## Run it") ?? "Read the project's README.md.");
    }

    private void WebApp(Arguments parsed, TemplateSource source, Questionnaire questionnaire, TemplateEngine dotnet, GeneratedProject project)
    {
        TemplateManifest manifest = source.Manifest(TemplateKind.WebApp);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = project.HasWorkspace ? Condition.False : Condition.True,
            ["scope"] = project.Scope,
            ["auth"] = project.Auth,
            ["ci"] = project.Ci ? Condition.True : Condition.False,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
        };
        Detected(project, manifest, detected);

        string? name = questionnaire.Text("--name", "App name", parsed.Name, null, AppNameProblem);
        Dictionary<string, string> answers = questionnaire.Answer(manifest, parsed.TemplateOptions, detected, _alwaysAsked);

        string folder = Path.Combine(project.WebFolder, "apps", name!);
        if (Directory.Exists(folder))
        {
            throw new LauncherException($"'{folder}' exists: choose another --name.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(answers);
        dotnet.EnsureInstalled(source, Version(source), project.Root);
        dotnet.Run(project.Root, [manifest.ShortName, "-n", name!, .. options]);

        Equivalents(parsed, Arguments.WebAppCommand, [manifest.ShortName, "-n", name!, .. options]);
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", answers));
        string readme = workspace ? Path.Combine(project.WebFolder, "README.md") : Path.Combine(folder, "README.md");
        string heading = workspace ? "## The first app of a project generated without one" : "## Join the stack";
        _console.MarkupLine("[bold]By hand[/] " + Markup.Escape(Path.GetRelativePath(project.Root, readme)) + ":");
        _console.Verbatim(MarkdownSection(readme, heading) ?? $"Read {readme}.");
    }

    private void WebPackage(Arguments parsed, TemplateSource source, Questionnaire questionnaire, TemplateEngine dotnet, GeneratedProject project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace Src/Web: add its first app with web-app, which creates it.");
        }

        TemplateManifest manifest = source.Manifest(TemplateKind.WebPackage);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = project.Scope };
        Detected(project, manifest, detected);

        string? name = questionnaire.Text("--name", "Package name", parsed.Name, null, PackageNameProblem);
        Dictionary<string, string> answers = questionnaire.Answer(manifest, parsed.TemplateOptions, detected, _alwaysAsked);

        IReadOnlyList<string> options = manifest.CommandArguments(answers);
        dotnet.EnsureInstalled(source, Version(source), project.Root);
        dotnet.Run(project.Root, [manifest.ShortName, "-n", name!, .. options]);

        Equivalents(parsed, Arguments.WebPackageCommand, [manifest.ShortName, "-n", name!, .. options]);
    }

    private void Detected(GeneratedProject project, TemplateManifest manifest, Dictionary<string, string> detected)
    {
        foreach (string symbol in detected.Keys.Where(symbol => manifest.Options.All(option => option.Symbol != symbol)).ToList())
        {
            detected.Remove(symbol);
        }

        string facts = string.Join(", ", manifest.Options
            .Where(option => detected.ContainsKey(option.Symbol))
            .Select(option => $"--{option.CliName} {detected[option.Symbol]}"));
        _console.MarkupLine($"[grey]Read from {Markup.Escape(project.Name)}: {Markup.Escape(facts)}[/]");
    }

    private void Equivalents(Arguments parsed, string command, IReadOnlyList<string> dotnetNew)
    {
        List<string> launcher = [command, .. dotnetNew.Skip(1), "--yes"];
        if (parsed.TemplateSource is { } source)
        {
            launcher.AddRange(["--template-source", source]);
        }

        _console.MarkupLine("[bold]Done.[/] The same, without questions:");
        _console.Verbatim("  " + TemplateEngine.CommandLine(Invocation, launcher));
        _console.Verbatim("  " + TemplateEngine.CommandLine("dotnet new", dotnetNew));
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex ProjectName();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}

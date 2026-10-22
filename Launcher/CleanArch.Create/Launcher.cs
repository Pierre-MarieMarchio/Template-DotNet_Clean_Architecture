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
    private readonly IProcessRunner _processes;

    public Launcher(IAnsiConsole console, string workingDirectory, bool interactive)
        : this(console, workingDirectory, interactive, new ProcessRunner())
    {
    }

    public Launcher(IAnsiConsole console, string workingDirectory, bool interactive, IProcessRunner processes)
    {
        _console = console;
        _workingDirectory = workingDirectory;
        _interactive = interactive;
        _processes = processes;
    }

    public int Run(IReadOnlyList<string> arguments)
    {
        Conversation conversation = new PlainConversation(_console);
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
            conversation = questionnaire.Conversation;
            var dotnet = new TemplateEngine(
                _console,
                parsed.Hive is { } hive ? Path.GetFullPath(hive, _workingDirectory) : null,
                conversation.EchoesTheTemplateEngine,
                _processes);
            var session = new Session(parsed, source, questionnaire, dotnet, source.Manifest(TemplateKind.Project).Version);
            GeneratedProject? project = GeneratedProject.Find(_workingDirectory);

            switch (parsed.Command ?? Choose(project, session))
            {
                case Arguments.WebAppCommand:
                    WebApp(session, Required(project, Arguments.WebAppCommand));
                    break;
                case Arguments.WebPackageCommand:
                    WebPackage(session, Required(project, Arguments.WebPackageCommand));
                    break;
                default:
                    New(session);
                    break;
            }

            return 0;
        }
        catch (LauncherException error)
        {
            conversation.Fail(error.Message);
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

    private static Asked Reviewed(
        Conversation conversation,
        Func<Asked?, Asked> ask,
        Func<Asked, IEnumerable<KeyValuePair<string, string>>> recap,
        Func<Asked, string> subject)
    {
        Asked asked = ask(null);
        while (!conversation.Review(subject(asked), [.. recap(asked)]))
        {
            asked = ask(asked);
        }

        return asked;
    }

    private static Asked AskItem(
        Session session,
        TemplateManifest manifest,
        string label,
        Func<string, string?> problem,
        IReadOnlyDictionary<string, string> detected,
        string subject) =>
        Reviewed(
            session.Conversation,
            previous => new Asked(
                session.Questionnaire.Text("--name", label, previous is null ? session.Parsed.Name : null, previous?.Name, problem),
                null,
                session.Questionnaire.Answer(manifest, previous is null ? session.Parsed.TemplateOptions : _nothing, detected, _alwaysAsked, previous?.Options)),
            asked => [KeyValuePair.Create(label, asked.Name!), .. Questionnaire.Recap(manifest, asked.Options)],
            _ => subject);

    private static void Install(Session session, string workingDirectory)
    {
        string version = Version(session.Source);
        string package = $"{TemplateSource.PackageId} {version}";
        session.Conversation.Perform($"Installing {package}", $"{package} ready", () => session.Dotnet.EnsureInstalled(session.Source, version, workingDirectory));
    }

    private static void Detected(Conversation conversation, GeneratedProject project, TemplateManifest manifest, Dictionary<string, string> detected)
    {
        foreach (string symbol in detected.Keys.Where(symbol => manifest.Options.All(option => option.Symbol != symbol)).ToList())
        {
            detected.Remove(symbol);
        }

        string facts = string.Join(", ", manifest.Options
            .Where(option => detected.ContainsKey(option.Symbol))
            .Select(option => $"--{option.CliName} {detected[option.Symbol]}"));
        conversation.Note($"Read from {project.Name}: {facts}");
    }

    private static void Equivalents(Session session, string command, IReadOnlyList<string> dotnetNew)
    {
        List<string> launcher = [command, .. dotnetNew.Skip(1), "--yes"];
        if (session.Parsed.TemplateSource is { } source)
        {
            launcher.AddRange(["--template-source", source]);
        }

        session.Conversation.Equivalents([TemplateEngine.CommandLine(Invocation, launcher), TemplateEngine.CommandLine("dotnet new", dotnetNew)]);
    }

    private static string Choose(GeneratedProject? project, Session session)
    {
        if (project is null || !session.Questionnaire.Asks)
        {
            return Arguments.NewCommand;
        }

        OptionChoice[] commands =
        [
            new(Arguments.WebAppCommand, $"add a web app to {project.Name}"),
            new(Arguments.WebPackageCommand, $"add a package to {project.Name}'s web workspace"),
            new(Arguments.NewCommand, "create another project"),
        ];

        session.Conversation.Begin(project.Name, session.Version);
        return session.Conversation.Choose(
            "What next?",
            $"{project.Name} is a generated project.",
            [.. commands.Where(command => project.HasWorkspace || command.Value != Arguments.WebPackageCommand)],
            null);
    }

    private void New(Session session)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Project);
        Questionnaire questionnaire = session.Questionnaire;
        Arguments parsed = session.Parsed;
        session.Conversation.Begin("Create a project", session.Version);

        Asked asked = Reviewed(
            session.Conversation,
            previous =>
            {
                string? name = questionnaire.Text("--name", "Project name", previous is null ? parsed.Name : null, previous?.Name ?? manifest.DefaultName, ProjectNameProblem);
                string? output = (previous is null ? parsed.Output : null)
                    ?? (questionnaire.Asks && name is not null
                        ? questionnaire.Text("--output", "Output folder", null, Path.Combine(".", name), _ => null)
                        : name);
                return new Asked(name, output, questionnaire.Answer(manifest, previous is null ? parsed.TemplateOptions : _nothing, _nothing, ProjectAskedWhen, previous?.Options));
            },
            asked => [KeyValuePair.Create("Project name", asked.Name!), KeyValuePair.Create("Output folder", asked.Output!), .. Questionnaire.Recap(manifest, asked.Options)],
            asked => $"Generate {asked.Name}");
        string name = asked.Name!;
        string output = asked.Output!;

        string target = Path.GetFullPath(output, _workingDirectory);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new LauncherException($"'{target}' is not empty: choose another --output.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        Install(session, _workingDirectory);
        session.Conversation.Perform($"Generating {name}", $"Generated {name}", () => session.Dotnet.Run(_workingDirectory, [manifest.ShortName, "-n", name, "-o", target, .. options]));

        Equivalents(session, Arguments.NewCommand, [manifest.ShortName, "-n", name, "-o", output, .. options]);
        session.Conversation.Close(new Closing(
            null,
            [TemplateEngine.CommandLine("cd", [output])],
            MarkdownSection(Path.Combine(target, "README.md"), "## Run it") ?? "Read the project's README.md."));
    }

    private static void WebApp(Session session, GeneratedProject project)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.WebApp);
        session.Conversation.Begin($"Add a web app to {project.Name}", session.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = project.HasWorkspace ? Condition.False : Condition.True,
            ["scope"] = project.Scope,
            ["auth"] = project.Auth,
            ["ci"] = project.Ci ? Condition.True : Condition.False,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
        };
        Detected(session.Conversation, project, manifest, detected);

        Asked asked = AskItem(session, manifest, "App name", AppNameProblem, detected, $"Add it to {project.Name}");
        string name = asked.Name!;

        string folder = Path.Combine(project.WebFolder, "apps", name);
        if (Directory.Exists(folder))
        {
            throw new LauncherException($"'{folder}' exists: choose another --name.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        Install(session, project.Root);
        session.Conversation.Perform($"Adding {name}", $"Added {name}", () => session.Dotnet.Run(project.Root, [manifest.ShortName, "-n", name, .. options]));

        Equivalents(session, Arguments.WebAppCommand, [manifest.ShortName, "-n", name, .. options]);
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", asked.Options));
        string readme = workspace ? Path.Combine(project.WebFolder, "README.md") : Path.Combine(folder, "README.md");
        string heading = workspace ? "## The first app of a project generated without one" : "## Join the stack";
        session.Conversation.Close(new Closing(Path.GetRelativePath(project.Root, readme), [], MarkdownSection(readme, heading) ?? $"Read {readme}."));
    }

    private static void WebPackage(Session session, GeneratedProject project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace Src/Web: add its first app with web-app, which creates it.");
        }

        TemplateManifest manifest = session.Source.Manifest(TemplateKind.WebPackage);
        session.Conversation.Begin($"Add a package to {project.Name}'s web workspace", session.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = project.Scope };
        Detected(session.Conversation, project, manifest, detected);

        Asked asked = AskItem(session, manifest, "Package name", PackageNameProblem, detected, $"Add it to {project.Name}");
        string name = asked.Name!;

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        Install(session, project.Root);
        session.Conversation.Perform($"Adding {name}", $"Added {name}", () => session.Dotnet.Run(project.Root, [manifest.ShortName, "-n", name, .. options]));

        Equivalents(session, Arguments.WebPackageCommand, [manifest.ShortName, "-n", name, .. options]);
        session.Conversation.Close(null);
    }

    private sealed record Session(Arguments Parsed, TemplateSource Source, Questionnaire Questionnaire, TemplateEngine Dotnet, string? Version)
    {
        public Conversation Conversation => Questionnaire.Conversation;
    }

    private sealed record Asked(string? Name, string? Output, Dictionary<string, string> Options);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex ProjectName();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}

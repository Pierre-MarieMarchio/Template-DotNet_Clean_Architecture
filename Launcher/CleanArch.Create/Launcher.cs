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
        ["presentationName"] = Condition.Parse("(presentation != \"none\")"),
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
          --setup                      after new: commit, bootstrap, commit again and copy .env without asking
          --template-source <path>     install the templates from a checkout or a .nupkg instead of nuget.org
          --hive <folder>              the template engine's settings folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help

        Every option of the template is a flag of the same name, e.g. --auth none --examples none.
        Without a terminal, every answer is a flag, or --yes takes the defaults.
        """;

    private static readonly IReadOnlyDictionary<string, string> _nothing = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, Condition> _alwaysAsked = new Dictionary<string, Condition>();

    private const string _nameKey = "--name";
    private const string _outputKey = "--output";
    private const string _setupKey = "--setup";
    private const string _reviewKey = "--review";
    private const char _setupSeparator = '|';

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

            string command = parsed.Command ?? Choose(project, session);
            if (parsed.Setup && command != Arguments.NewCommand)
            {
                throw new LauncherException($"--setup sets up a project new generates; {command} takes no --setup.");
            }

            switch (command)
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

    private static Question Reviewed(Session session, Func<IReadOnlyDictionary<string, string>, IEnumerable<KeyValuePair<string, string>>> recap, Func<IReadOnlyDictionary<string, string>, string> subject) =>
        new(_reviewKey, (answers, _) =>
        {
            if (session.Conversation.Review(subject(answers), [.. recap(answers)]))
            {
                return Reply.Passed(null);
            }

            return session.Conversation.CanGoBack
                ? Reply.Backward
                : throw new LauncherException("Stopped before anything was written.");
        });

    private static Asked AskItem(
        Session session,
        TemplateManifest manifest,
        string label,
        Func<string, string?> problem,
        IReadOnlyDictionary<string, string> detected,
        string subject)
    {
        Dictionary<string, string> answers = session.Questionnaire.Answer(
            manifest,
            session.Parsed.TemplateOptions,
            detected,
            _alwaysAsked,
            [new(_nameKey, (_, remembered) => session.Questionnaire.Text("--name", label, session.Parsed.Name, remembered, problem))],
            [Reviewed(session, given => [KeyValuePair.Create(label, given.GetValueOrDefault(_nameKey) ?? string.Empty), .. Questionnaire.Recap(manifest, given)], _ => subject)]);

        return new Asked(answers.GetValueOrDefault(_nameKey), null, answers, []);
    }

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

    private static Question SetupQuestion(Session session) =>
        new(_setupKey, (_, remembered) =>
        {
            string[] every = [.. ProjectSetup.Steps.Select(step => step.Title)];
            if (session.Parsed.Setup)
            {
                return Reply.Passed(string.Join(_setupSeparator, every));
            }

            if (!session.Questionnaire.Asks)
            {
                return Reply.Passed(string.Empty);
            }

            IReadOnlyList<string>? chosen = session.Conversation.ChooseMany(
                "Set up the project now?",
                [.. ProjectSetup.Steps.Select(step => new OptionChoice(step.Title, step.Description))],
                remembered is null ? every : Steps(remembered));
            return chosen is null ? Reply.Backward : Reply.Answer(string.Join(_setupSeparator, chosen));
        });

    private static string[] Steps(string? chosen) => chosen?.Split(_setupSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static string Recalled(string[] setup) => setup.Length == 0 ? "Nothing" : string.Join(", ", setup);

    private static string? Without(string? section, IReadOnlyCollection<string> commands) =>
        section is null
            ? null
            : string.Join('\n', section.Split('\n').Where(line => !commands.Any(command => line.TrimStart().StartsWith(command, StringComparison.Ordinal))));

    private static void Equivalents(Session session, string command, IReadOnlyList<string> dotnetNew, bool setup = false)
    {
        List<string> launcher = [command, .. dotnetNew.Skip(1), "--yes"];
        if (setup)
        {
            launcher.Add("--setup");
        }

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
            null) ?? Arguments.NewCommand;
    }

    private void New(Session session)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Project);
        Questionnaire questionnaire = session.Questionnaire;
        Arguments parsed = session.Parsed;
        session.Conversation.Begin("Create a project", session.Version);

        Question[] before =
        [
            new(_nameKey, (_, remembered) => questionnaire.Text("--name", "Project name", parsed.Name, remembered ?? manifest.DefaultName, ProjectNameProblem)),
            new(_outputKey, (answers, _) =>
            {
                string? named = answers.GetValueOrDefault(_nameKey);
                if (parsed.Output is not null || !questionnaire.Asks || named is null)
                {
                    return Reply.Passed(parsed.Output ?? named);
                }

                return questionnaire.Text("--output", "Output folder", null, Path.Combine(".", named), _ => null);
            }),
        ];
        Question[] after =
        [
            SetupQuestion(session),
            Reviewed(
                session,
                given =>
                [
                    KeyValuePair.Create("Project name", given.GetValueOrDefault(_nameKey) ?? string.Empty),
                    KeyValuePair.Create("Output folder", given.GetValueOrDefault(_outputKey) ?? string.Empty),
                    .. Questionnaire.Recap(manifest, given),
                    KeyValuePair.Create("Set up", Recalled(Steps(given.GetValueOrDefault(_setupKey)))),
                ],
                given => $"Generate {given.GetValueOrDefault(_nameKey)}"),
        ];
        Dictionary<string, string> answers = questionnaire.Answer(manifest, parsed.TemplateOptions, _nothing, ProjectAskedWhen, before, after);
        var asked = new Asked(answers.GetValueOrDefault(_nameKey), answers.GetValueOrDefault(_outputKey), answers, Steps(answers.GetValueOrDefault(_setupKey)));
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
        List<SetupOutcome> outcomes = SetUp(session, target, asked.Setup);

        Equivalents(session, Arguments.NewCommand, [manifest.ShortName, "-n", name, "-o", output, .. options], asked.Setup.Count == ProjectSetup.Steps.Count);
        string? section = Without(
            MarkdownSection(Path.Combine(target, "README.md"), "## Run it"),
            [.. outcomes.Where(outcome => outcome.Status == SetupStatus.Done && outcome.Step.Replaces is not null).Select(outcome => outcome.Step.Replaces!)]);
        IEnumerable<string> left = outcomes
            .SelectMany(outcome => outcome.Rerun)
            .Distinct(StringComparer.Ordinal)
            .Where(command => section?.Contains(command, StringComparison.Ordinal) != true);
        session.Conversation.Close(new Closing(
            null,
            [TemplateEngine.CommandLine("cd", [output]), .. left],
            section ?? "Read the project's README.md."));
    }

    private List<SetupOutcome> SetUp(Session session, string target, IReadOnlyList<string> chosen)
    {
        var setup = new ProjectSetup(_processes, target, Version(session.Source), session.Conversation.EchoesTheTemplateEngine ? _console.Verbatim : null);
        var outcomes = new List<SetupOutcome>();
        foreach (SetupStep step in ProjectSetup.Steps.Where(step => chosen.Contains(step.Title)))
        {
            SetupOutcome outcome = session.Conversation.Spin(step.Running, () => setup.Run(step, outcomes));
            session.Conversation.Reported(outcome.Text, outcome.Status == SetupStatus.Done, outcome.Why, outcome.Rerun);
            outcomes.Add(outcome);
        }

        return outcomes;
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

    private sealed record Asked(string? Name, string? Output, Dictionary<string, string> Options, IReadOnlyList<string> Setup);

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex ProjectName();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}

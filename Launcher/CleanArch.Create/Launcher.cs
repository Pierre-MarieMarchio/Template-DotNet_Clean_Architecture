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
        ["webRendering"] = Condition.Parse("(web != \"none\")"),
        ["presentationName"] = Condition.Parse("(presentation != \"none\")"),
    };

    private const string _help =
        """
        Usage: dnx CleanArch.Create [new|web-app|web-package|presentation] [options] [--<template option> <value>]...

        Commands
          new            create a project (what runs when you're not inside one)
          web-app        add a web app to the project you're in;
                         a Blazor WebAssembly app also makes the edits its README lists
          web-package    add a shared package to that project's web workspace
          presentation   add a presentation project beside that project's API,
                         make the edits its README lists, then create its pages

        Options
          -n, --name <name>            the project, app, package or presentation name
          -o, --output <folder>        where to create the project (default: ./<name>)
          -y, --yes                    use the default for every option you don't pass
          --setup                      after new, run every setup step without asking: commit, bootstrap, commit, copy .env
          --template-source <path>     use a checkout of the template or a .nupkg instead of the published package
          --hive <folder>              keep the template engine's settings in this folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help

        Without a terminal (in CI or a pipe), pass every answer as an option, or add --yes.
        """;

    private static readonly IReadOnlyDictionary<string, string> _nothing = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, Condition> _alwaysAsked = new Dictionary<string, Condition>();

    private const string _nameKey = "--name";
    private const string _outputKey = "--output";
    private const string _setupKey = "--setup";
    private const string _reviewKey = "--review";
    private const char _setupSeparator = '|';
    private const string _itemNameSymbol = "name";

    internal const string PresentationJoinTask = "presentation-join";
    internal const string WebJoinTask = "web-join";
    internal const string PresentationScaffoldTask = "presentation-scaffold";
    internal const string JoiningSection = "## Joining a project generated without one";
    internal const string PresentationCreateSection = "## Create the project";

    private readonly IAnsiConsole _console;
    private readonly string _workingDirectory;
    private readonly bool _interactive;
    private readonly IProcessRunner _processes;
    private volatile bool _settingUp;
    private volatile bool _stopped;

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

    public const int StoppedExitCode = 130;

    public bool Interrupt()
    {
        _stopped = _settingUp;
        return _stopped;
    }

    public int Run(IReadOnlyList<string> arguments)
    {
        Conversation conversation = new PlainConversation(_console);
        try
        {
            Arguments parsed = Arguments.Parse(arguments);
            TemplateSource source = parsed.TemplateSource is { } path
                ? TemplateSource.FromPath(Path.GetFullPath(path, _workingDirectory))
                : TemplateSource.Embedded();
            if (parsed.Help)
            {
                _console.Verbatim(_help + Environment.NewLine + Environment.NewLine + TemplateOptions(source.Manifest(TemplateKind.Project)));
                return 0;
            }
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
                case Arguments.PresentationCommand:
                    Presentation(session, Required(project, Arguments.PresentationCommand));
                    break;
                default:
                    New(session);
                    break;
            }

            return _stopped ? StoppedExitCode : 0;
        }
        catch (LauncherException error)
        {
            conversation.Fail(error.Message);
            return 1;
        }
    }

    private static string TemplateOptions(TemplateManifest manifest)
    {
        IEnumerable<string> lines = manifest.Options.Select(option =>
        {
            string values = option.Kind switch
            {
                OptionKind.Choice => string.Join('|', Offer.For(manifest, option, _nothing, ProjectAskedWhen).Values),
                OptionKind.Bool => "true|false",
                _ => "<value>",
            };
            string flag = $"  --{option.CliName} {values}";
            string fallback = option.DefaultValue is null ? string.Empty : $" (default: {option.DefaultValue})";
            return $"{flag,-46} {option.Label}{fallback}";
        });

        return string.Join(Environment.NewLine, lines.Prepend($"Project options, asked one by one when you don't pass them ({manifest.ShortName})"));
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
            $"{command} only works inside a project made with this template, and there isn't one here or in any folder above (it looks for a .sln next to Tools/Tasks.cs).");

    private static string Version(TemplateSource source) =>
        source.Manifest(TemplateKind.Project).Version
            ?? throw new LauncherException("This template source has no templateVersion, so there's no package version to install.");

    private string? OutputProblem(string folder)
    {
        string target = Path.GetFullPath(folder, _workingDirectory);
        return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() ? "that folder already has files in it." : null;
    }

    private static string? ProjectNameProblem(string name) =>
        ProjectName().IsMatch(name) ? null : "use letters, digits and dots, with each part starting with a letter, like Acme.Shop.";

    private static string? AppNameProblem(string name) =>
        AppName().IsMatch(name) ? null : "use PascalCase, like BackOffice. It becomes the app's folder, service and port variable.";

    private static string? PackageNameProblem(string name) =>
        PackageName().IsMatch(name) ? null : "use letters, digits and dashes, starting with a letter, like tokens.";

    private static Question Reviewed(Session session, Func<IReadOnlyDictionary<string, string>, IEnumerable<KeyValuePair<string, string>>> recap, Func<IReadOnlyDictionary<string, string>, string> subject) =>
        new(_reviewKey, (answers, _) =>
        {
            if (session.Conversation.Review(subject(answers), [.. recap(answers)]))
            {
                return Reply.Passed(null);
            }

            return session.Conversation.CanGoBack
                ? Reply.Backward
                : throw new LauncherException("Stopped. Nothing was written.");
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
            [new(_nameKey, (_, remembered) => session.Questionnaire.Text("--name", label, session.Parsed.Name, remembered ?? manifest.DefaultName, problem))],
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
        conversation.Note($"Taken from {project.Name}: {facts}");
    }

    private static Question SetupQuestion(Session session) =>
        new(_setupKey, (answers, remembered) =>
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
                [.. ProjectSetup.Steps.Select(step => new OptionChoice(step.Title, step == ProjectSetup.Bootstrap ? BootstrapDescription(session, answers) : step.Description))],
                remembered is null ? every : Steps(remembered));
            return chosen is null ? Reply.Backward : Reply.Answer(string.Join(_setupSeparator, chosen));
        });

    private static string BootstrapDescription(Session session, IReadOnlyDictionary<string, string> answers)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Project);
        return ProjectSetup.BootstrapDescription(symbol => Condition.IsTruthy(manifest.Resolve(symbol, answers)));
    }

    private static string[] Steps(string? chosen) => chosen?.Split(_setupSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static string Recalled(string[] setup) => setup.Length == 0 ? "Nothing" : string.Join(", ", setup);

    private static string? Without(string? section, IReadOnlyCollection<string> commands) =>
        section is null
            ? null
            : string.Join('\n', section.Split('\n').Where(line => !commands.Any(command => line.TrimStart().StartsWith(command, StringComparison.Ordinal))));

    private static void Equivalents(Session session, string command, IReadOnlyList<string> dotnetNew, bool setup = false, IReadOnlyList<string>? then = null)
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

        List<string> engine = [.. dotnetNew];
        if (session.Parsed.Hive is { } hive)
        {
            launcher.AddRange(["--hive", hive]);
            engine.AddRange(["--debug:custom-hive", hive]);
        }

        string engineLine = TemplateEngine.CommandLine("dotnet new", engine);
        session.Conversation.Equivalents(
            session.Questionnaire.Asks ? [TemplateEngine.CommandLine(Invocation, launcher), engineLine, .. then ?? []] : [engineLine, .. then ?? []],
            session.Questionnaire.Asks);
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
            new(Arguments.PresentationCommand, $"add a presentation project beside {project.Name}'s API"),
            new(Arguments.NewCommand, "start a new project somewhere else"),
        ];

        session.Conversation.Begin(project.Name, session.Version);
        return session.Conversation.Choose(
            "What do you want to do?",
            $"You're inside {project.Name}, a project made with this template.",
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

                return questionnaire.Text("--output", "Output folder", null, "./" + named, OutputProblem);
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
                    KeyValuePair.Create("Setup steps", Recalled(Steps(given.GetValueOrDefault(_setupKey)))),
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
            throw new LauncherException($"'{target}' already has files in it. Pick another folder with --output.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        Install(session, _workingDirectory);
        session.Conversation.Perform($"Generating {name}", $"Generated {name}", () => session.Dotnet.Run(
                _workingDirectory,
                [manifest.ShortName, "-n", name, "-o", target, .. options],
                asked.Setup.Contains(ProjectSetup.Bootstrap.Title) ? [ProjectSetup.BootstrapTask] : null));
        List<SetupOutcome> outcomes = SetUp(session, target, asked.Setup);

        Equivalents(session, Arguments.NewCommand, [manifest.ShortName, "-n", name, "-o", output, .. options], asked.Setup.Count == ProjectSetup.Steps.Count);
        string[] left = [.. ProjectSetup.Left(outcomes)];
        string? section = Without(
            MarkdownSection(Path.Combine(target, "README.md"), "## Run it"),
            [.. outcomes.Where(outcome => outcome.Status == SetupStatus.Done && outcome.Step.Replaces is not null).Select(outcome => outcome.Step.Replaces!), .. left]);
        session.Conversation.Close(new Closing(
            null,
            [TemplateEngine.CommandLine("cd", [output]), .. left],
            section ?? "Read the project's README.md.",
            outcomes.Any(outcome => outcome.Status != SetupStatus.Done)));
    }

    private List<SetupOutcome> SetUp(Session session, string target, IReadOnlyList<string> chosen)
    {
        var setup = new ProjectSetup(_processes, target, Version(session.Source), session.Conversation.EchoesTheTemplateEngine ? _console.Verbatim : null);
        var outcomes = new List<SetupOutcome>();
        _settingUp = true;
        try
        {
            foreach (SetupStep step in ProjectSetup.Steps.Where(step => chosen.Contains(step.Title)))
            {
                SetupOutcome outcome = _stopped ? setup.Stopped(step) : session.Conversation.Spin(step.Running, () => setup.Run(step, outcomes));
                session.Conversation.Reported(outcome.Text, outcome.Status == SetupStatus.Done, outcome.Why, outcome.Rerun);
                outcomes.Add(outcome);
            }
        }
        finally
        {
            _settingUp = false;
        }

        return outcomes;
    }

    private void WebApp(Session session, GeneratedProject project)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.WebApp);
        session.Conversation.Begin($"Add a web app to {project.Name}", session.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = project.HasWorkspace ? Condition.False : Condition.True,
            ["scope"] = project.Scope,
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = project.Files ? Condition.True : Condition.False,
            ["hasClient"] = project.HasClient ? Condition.True : Condition.False,
            ["ci"] = project.Ci ? Condition.True : Condition.False,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
        };
        Detected(session.Conversation, project, manifest, detected);

        Asked asked = AskItem(
            session,
            manifest,
            "App name",
            name => AppNameProblem(name) ?? (Directory.Exists(Path.Combine(project.WebFolder, "apps", name)) ? "there's already an app with that name." : null),
            detected, $"Add it to {project.Name}");
        string name = asked.Name!;

        string folder = Path.Combine(project.WebFolder, "apps", name);
        if (Directory.Exists(folder))
        {
            throw new LauncherException($"There's already an app in '{folder}'. Pick another name with --name.");
        }

        if (manifest.Resolve("framework", asked.Options) == "blazor-wasm" && project.BlazorWebAssemblyApp is { } existing)
        {
            throw new LauncherException($"{project.Name} already has a Blazor WebAssembly app, Src/Web/apps/{existing}. This version adds only one per project.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", asked.Options));
        Install(session, project.Root);
        session.Conversation.Perform(
            $"Adding {name}",
            $"Added {name}",
            () => session.Dotnet.Run(project.Root, [manifest.ShortName, "-n", name, .. options], workspace ? null : [WebJoinTask]));

        string appReadme = Path.Combine(folder, "README.md");
        string[] join = ["run", "Tools/Tasks.cs", WebJoinTask, name];
        string[] joinLines = MarkdownSection(appReadme, JoiningSection) is null ? [] : [TemplateEngine.CommandLine("dotnet", join)];
        bool joined = joinLines.Length == 0 || (!workspace && RunTask(session, project.Root, "Join", "Making the edits its README lists outside its folder", join));

        Equivalents(session, Arguments.WebAppCommand, [manifest.ShortName, "-n", name, .. options], then: joinLines);
        string readme = workspace ? Path.Combine(project.WebFolder, "README.md") : appReadme;
        string heading = workspace ? "## The first app of a project generated without one" : joined ? "## Join the stack" : JoiningSection;
        session.Conversation.Close(new Closing(Path.GetRelativePath(project.Root, readme).Replace(Path.DirectorySeparatorChar, '/'), [], MarkdownSection(readme, heading) ?? $"Read {readme}.", StepsLeft: !workspace && !joined));
    }

    private static void WebPackage(Session session, GeneratedProject project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace yet. Add a web app first with web-app: it creates Src/Web.");
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

    private void Presentation(Session session, GeneratedProject project)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Presentation);
        session.Conversation.Begin($"Add a presentation project to {project.Name}", session.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = project.Files ? Condition.True : Condition.False,
            ["hasClient"] = project.HasClient ? Condition.True : Condition.False,
            ["database"] = project.Database,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
        };
        Detected(session.Conversation, project, manifest, detected);

        Asked asked = AskItem(session, manifest, "Presentation name", name => PresentationNameProblem(manifest, project, name), detected, $"Add it to {project.Name}");
        string name = asked.Name!;
        string folder = $"{project.Name}.{name}";

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        Install(session, project.Root);
        session.Conversation.Perform($"Adding {name}", $"Added {name}", () => session.Dotnet.Run(project.Root, [manifest.ShortName, "-n", name, .. options], [PresentationJoinTask, PresentationScaffoldTask]));

        string[] join = ["run", "Tools/Tasks.cs", PresentationJoinTask, folder];
        string[] scaffold = ["run", "Tools/Tasks.cs", PresentationScaffoldTask, folder];
        string joinLine = TemplateEngine.CommandLine("dotnet", join);
        string scaffoldLine = TemplateEngine.CommandLine("dotnet", scaffold);
        bool joined = RunTask(session, project.Root, "Join", "Making the edits its README lists outside its folders", join);
        bool created = joined && RunTask(session, project.Root, "Scaffold", "Creating it with its framework's dotnet new template, which needs the network", scaffold);
        if (!joined)
        {
            session.Conversation.Reported("Scaffold skipped", false, "Skipped because the join step didn't go through.", [scaffoldLine]);
        }

        Equivalents(session, Arguments.PresentationCommand, [manifest.ShortName, "-n", name, .. options], then: [joinLine, scaffoldLine]);
        string readme = Path.Combine(project.PresentationFolder, folder, "README.md");
        string shown = $"Src/Presentation/{folder}/README.md";
        if (created)
        {
            session.Conversation.Close(new Closing(
                null,
                [],
                $"Commit Src/Presentation/{folder} and its tests: from now on the pages are this project's own. {shown} says how the project was created."));
            return;
        }

        string heading = joined ? PresentationCreateSection : JoiningSection;
        session.Conversation.Close(new Closing(shown, [], MarkdownSection(readme, heading) ?? $"Read {shown}.", StepsLeft: true));
    }

    private bool RunTask(Session session, string root, string title, string running, IReadOnlyList<string> arguments)
    {
        string command = TemplateEngine.CommandLine("dotnet", arguments);
        ProcessResult result = session.Conversation.Spin(
            running,
            () => _processes.Run(new ProcessCall("dotnet", root, arguments, _nothing), session.Conversation.EchoesTheTemplateEngine ? _console.Verbatim : null));
        bool done = result.ExitCode == 0;
        session.Conversation.Reported(title + (done ? " done" : " failed"), done, done ? null : ProjectSetup.Why(command, result), done ? [] : [command]);
        return done;
    }

    private static string? PresentationNameProblem(TemplateManifest manifest, GeneratedProject project, string name)
    {
        if (!AppName().IsMatch(name))
        {
            return $"use PascalCase, like Portal. {project.Name}.<name> becomes the project's name, folder and service.";
        }

        var named = new Dictionary<string, string>(StringComparer.Ordinal) { [_itemNameSymbol] = name };
        if (manifest.Refusals.FirstOrDefault(refusal => refusal.When.Symbols.Contains(_itemNameSymbol) && manifest.Holds(refusal.When, named)) is { } refused)
        {
            return refused.Explanation;
        }

        string folder = Path.Combine(project.PresentationFolder, $"{project.Name}.{name}");
        return Directory.Exists(folder) ? "there's already a presentation project with that name." : null;
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

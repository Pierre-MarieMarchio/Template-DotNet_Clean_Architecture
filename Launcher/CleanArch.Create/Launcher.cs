using System.Text.RegularExpressions;
using CleanArch.Create.Projects;
using CleanArch.Tooling;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Prompts;
using Spectre.Console;

namespace CleanArch.Create;

public sealed partial class Launcher
{
    public const string Invocation = "dnx CleanArch.Create";

    public const string InvocationWithArguments = Invocation + " " + Arguments.Separator;

    public static readonly IReadOnlyDictionary<string, Condition> ProjectAskedWhen = new Dictionary<string, Condition>(StringComparer.Ordinal)
    {
        ["webName"] = Condition.Parse("(web != \"none\")"),
        ["webAudience"] = Condition.Parse("(web != \"none\")"),
        ["webRendering"] = Condition.Parse("(web != \"none\")"),
    };

    public static readonly IReadOnlyList<ApiCaller> ApiCallers =
    [
        new("the web app", Condition.Parse("(anyWeb)")),
        new("the desktop client", Condition.Parse("(presentationAvalonia || presentationWpf)")),
        new("the MCP server under local accounts", Condition.Parse("(presentationMcp && localAuth)")),
    ];

    public static readonly IReadOnlyDictionary<string, string> PresentationNames = ItemAdditions.PresentationNames;

    private const string _help =
        $"""
        Usage: {Invocation} [-- [new|web-app|web-package|presentation] [options] [--<template option> <value>]...]

        dnx takes -y, --yes, --help and --version for itself: put the launcher's own arguments after --.

        Commands
          new            create a project (what runs when you're not inside one)
          web-app        add a web app to the project you're in;
                         a Blazor WebAssembly app also makes the edits its README lists
          web-package    add a shared package to that project's web workspace
          presentation   add a presentation project to that project,
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
    private const string _hostsAddedKey = "--hosts-added";
    private const string _presentationSymbol = "presentation";
    private const string _apiChoice = "api";
    private const string _workerChoice = "worker";
    private const char _setupSeparator = '|';

    internal const string PresentationJoinTask = ItemAdditions.PresentationJoinTask;
    internal const string WebJoinTask = ItemAdditions.WebJoinTask;
    internal const string PresentationScaffoldTask = ItemAdditions.PresentationScaffoldTask;
    internal const string JoiningSection = ItemAdditions.JoiningSection;
    internal const string PresentationCreateSection = ItemAdditions.PresentationCreateSection;

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
                : TemplateSource.Embedded(typeof(Launcher).Assembly);
            if (parsed.Help)
            {
                _console.Verbatim(Help(parsed.Command, source));
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
            ProjectFacts? project = GeneratedProject.Find(_workingDirectory);

            string command = parsed.Command ?? Choose(project, session);
            if (parsed.Setup && command != Arguments.NewCommand)
            {
                throw new LauncherException($"--setup sets up a project new generates; {command} takes no --setup.");
            }

            switch (command)
            {
                case Arguments.WebAppCommand:
                    Additions(session).WebApp(Required(project, Arguments.WebAppCommand));
                    break;
                case Arguments.WebPackageCommand:
                    Additions(session).WebPackage(Required(project, Arguments.WebPackageCommand));
                    break;
                case Arguments.PresentationCommand:
                    Additions(session).Presentation(Required(project, Arguments.PresentationCommand));
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

    private static string Help(string? command, TemplateSource source)
    {
        (TemplateKind kind, string subject, IReadOnlyDictionary<string, Condition> askedWhen) = command switch
        {
            Arguments.WebAppCommand => (TemplateKind.WebApp, "web app", _alwaysAsked),
            Arguments.WebPackageCommand => (TemplateKind.WebPackage, "web package", _alwaysAsked),
            Arguments.PresentationCommand => (TemplateKind.Presentation, "presentation project", _alwaysAsked),
            _ => (TemplateKind.Project, "project", ProjectAskedWhen),
        };

        string options = TemplateOptions(source.Manifest(kind), subject, askedWhen);
        return command is null or Arguments.NewCommand
            ? _help + Environment.NewLine + Environment.NewLine + options
            : CommandHelp(command) + Environment.NewLine + Environment.NewLine + options;
    }

    private static string CommandHelp(string command)
    {
        (string purpose, string subject) = command switch
        {
            Arguments.WebAppCommand => ("adds a web app to the project you're in; a Blazor WebAssembly app also makes the edits its README lists.", "app"),
            Arguments.WebPackageCommand => ("adds a shared package to the web workspace of the project you're in.", "package"),
            _ => ("adds a presentation project to the project you're in, makes the edits its README lists, then creates its pages.", "presentation"),
        };

        return $"""
        Usage: {InvocationWithArguments} {command} [options] [--<template option> <value>]...

        {command} {purpose}

        Options
          -n, --name <name>            the {subject}'s name
          -y, --yes                    use the default for every option you don't pass
          --template-source <path>     use a checkout of the template or a .nupkg instead of the published package
          --hive <folder>              keep the template engine's settings in this folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help
        """;
    }

    private static string TemplateOptions(TemplateManifest manifest, string subject, IReadOnlyDictionary<string, Condition> askedWhen)
    {
        IEnumerable<string> lines = manifest.Options.Select(option =>
        {
            string values = option.Kind switch
            {
                OptionKind.Choice => string.Join('|', Offer.For(manifest, option, _nothing, askedWhen).Values),
                OptionKind.Bool => "true|false",
                _ => "<value>",
            };
            string flag = $"  --{option.CliName} {values}";
            string fallback = option.DefaultValue is null ? string.Empty : $" (default: {option.DefaultValue})";
            string repeated = option.AllowsMany ? $", one --{option.CliName} per value" : string.Empty;
            return $"{flag,-46} {option.Label}{repeated}{fallback}";
        });

        return string.Join(Environment.NewLine, lines.Prepend($"{char.ToUpperInvariant(subject[0])}{subject[1..]} options, asked one by one when you don't pass them ({manifest.ShortName})"));
    }

    internal static string? MarkdownSection(string path, string heading) => CleanArch.Tooling.MarkdownSection.Of(path, heading);

    private static ProjectFacts Required(ProjectFacts? project, string command) =>
        project ?? throw new LauncherException(
            $"{command} only works inside a project made with this template, and there isn't one here or in any folder above (it looks for a .slnx or a .sln next to Tools/Tasks.cs).");

    private string? OutputProblem(string folder)
    {
        string target = Path.GetFullPath(folder, _workingDirectory);
        return Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() ? "that folder already has files in it." : null;
    }

    private static string? ProjectNameProblem(TemplateManifest manifest, string name) =>
        ProjectName().IsMatch(name) ? ItemAdditions.NameRefusal(manifest, _nothing, name) : "use the letters A to Z, digits and dots, with each part starting with a letter, like Acme.Shop.";

    private static Question Reviewed(Session session, Func<IReadOnlyDictionary<string, string>, IEnumerable<KeyValuePair<string, string>>> recap, Func<IReadOnlyDictionary<string, string>, string> subject) =>
        ItemAdditions.Reviewed(session.Conversation, recap, subject);

    public static IReadOnlyList<string> CallersOfTheApiAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        answers.TryGetValue(_presentationSymbol, out string? chosen) && !TemplateOption.Values(chosen).Contains(_apiChoice)
            ? [.. ApiCallers.Where(caller => manifest.Holds(caller.When, answers)).Select(caller => caller.Who)]
            : [];

    public static bool WorkerAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers) =>
        answers.TryGetValue(_presentationSymbol, out string? chosen)
            && !TemplateOption.Values(chosen).Any(value => value is _apiChoice or _workerChoice)
            && !ApiCallers.Any(caller => manifest.Holds(caller.When, answers));

    public static Dictionary<string, string> WithTheHostsAdded(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers)
    {
        var completed = new Dictionary<string, string>(answers, StringComparer.Ordinal);
        if (CallersOfTheApiAdded(manifest, answers).Count > 0)
        {
            completed[_presentationSymbol] = PresentationOption(manifest).Joined([.. TemplateOption.Values(answers[_presentationSymbol]), _apiChoice]);
        }
        else if (WorkerAdded(manifest, answers))
        {
            completed[_presentationSymbol] = PresentationOption(manifest).Joined([.. TemplateOption.Values(answers[_presentationSymbol]), _workerChoice]);
        }

        return completed;
    }

    private static TemplateOption PresentationOption(TemplateManifest manifest) =>
        manifest.Options.Single(option => option.Symbol == _presentationSymbol);

    private static Question HostsAddedQuestion(Session session, TemplateManifest manifest) =>
        new(_hostsAddedKey, (answers, _) =>
        {
            string? why = CallersOfTheApiAdded(manifest, answers) is { Count: > 0 } callers
                ? $"the API added for you: {string.Join(" and ", callers)} {(callers.Count == 1 ? "calls" : "call")} it"
                : WorkerAdded(manifest, answers) ? "the worker added for you: the jobs run in it" : null;
            if (why is not null)
            {
                TemplateOption presentation = PresentationOption(manifest);
                session.Conversation.Answered(presentation.Label, Questionnaire.Shown(presentation, WithTheHostsAdded(manifest, answers)[_presentationSymbol]), why);
            }

            return Reply.Passed(null);
        });

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
                string.Empty,
                [.. ProjectSetup.Steps.Select(step => new OptionChoice(step.Title, step == ProjectSetup.Bootstrap ? BootstrapDescription(session, answers) : step.Description))],
                remembered is null ? every : Steps(remembered),
                atLeastOne: false);
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

    private static void Equivalents(Session session, string command, IReadOnlyList<string> dotnetNew, bool setup = false) =>
        ItemAdditions.Equivalents(session.Conversation, session.Questionnaire.Asks, InvocationWithArguments, command, dotnetNew, session.Parsed.TemplateSource, session.Parsed.Hive, setup);

    private static string Choose(ProjectFacts? project, Session session)
    {
        if (project is null || !session.Questionnaire.Asks)
        {
            return Arguments.NewCommand;
        }

        OptionChoice[] commands =
        [
            new(Arguments.WebAppCommand, $"add a web app to {project.Name}"),
            new(Arguments.WebPackageCommand, $"add a package to {project.Name}'s web workspace"),
            new(Arguments.PresentationCommand, $"add a presentation project to {project.Name}"),
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
            new(_nameKey, (_, remembered) => questionnaire.Text("--name", "Project name", parsed.Name, remembered ?? manifest.DefaultName, name => ProjectNameProblem(manifest, name))),
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
            HostsAddedQuestion(session, manifest),
            SetupQuestion(session),
            Reviewed(
                session,
                given =>
                [
                    KeyValuePair.Create("Project name", given.GetValueOrDefault(_nameKey) ?? string.Empty),
                    KeyValuePair.Create("Output folder", given.GetValueOrDefault(_outputKey) ?? string.Empty),
                    .. Questionnaire.Recap(manifest, WithTheHostsAdded(manifest, given)),
                    KeyValuePair.Create("Setup steps", Recalled(Steps(given.GetValueOrDefault(_setupKey)))),
                ],
                given => $"Generate {given.GetValueOrDefault(_nameKey)}"),
        ];
        Dictionary<string, string> answers = WithTheHostsAdded(manifest, questionnaire.Answer(manifest, parsed.TemplateOptions, _nothing, ProjectAskedWhen, before, after));
        var asked = new Asked(answers.GetValueOrDefault(_nameKey), answers.GetValueOrDefault(_outputKey), answers, Steps(answers.GetValueOrDefault(_setupKey)));
        string name = asked.Name!;
        string output = asked.Output!;

        string target = Path.GetFullPath(output, _workingDirectory);
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new LauncherException($"'{target}' already has files in it. Pick another folder with --output.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        ItemAdditions.Install(session.Conversation, session.Dotnet, session.Source, session.Version, _workingDirectory);
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
        var setup = new ProjectSetup(_processes, target, session.Version ?? throw new LauncherException("This template source has no templateVersion, so there's no package version to install."), session.Conversation.EchoesTheTemplateEngine ? _console.Verbatim : null);
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

    private ItemAdditions Additions(Session session) =>
        new(new AdditionContext(
            session.Questionnaire,
            session.Source,
            session.Dotnet,
            _processes,
            _console,
            new AdditionArguments(session.Parsed.Name, session.Parsed.TemplateOptions, session.Parsed.TemplateSource, session.Parsed.Hive),
            session.Version,
            InvocationWithArguments));

    public sealed record ApiCaller(string Who, Condition When);

    private sealed record Session(Arguments Parsed, TemplateSource Source, Questionnaire Questionnaire, TemplateEngine Dotnet, string? Version)
    {
        public Conversation Conversation => Questionnaire.Conversation;
    }

    private sealed record Asked(string? Name, string? Output, Dictionary<string, string> Options, IReadOnlyList<string> Setup);

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*$")]
    private static partial Regex ProjectName();

}

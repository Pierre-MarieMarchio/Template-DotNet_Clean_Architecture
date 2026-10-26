namespace CleanArch.Create.Projects;

public enum SetupStatus
{
    Done,
    Failed,
    Skipped,
}

public sealed record SetupStep(string Title, string Description, string Running, string? Replaces);

public sealed record SetupOutcome(SetupStep Step, SetupStatus Status, string? Why, IReadOnlyList<string> Rerun)
{
    public string Text => Step.Title + " " + Status.ToString().ToLowerInvariant();
}

public sealed class ProjectSetup
{
    public const string BootstrapTask = "bootstrap";
    public const string BootstrapCommand = "dotnet run Tools/Tasks.cs " + BootstrapTask;
    public const string EnvironmentCommand = "cp .env.example .env";
    public const string BootstrapMessage = "chore: bootstrap";

    public static readonly SetupStep FirstCommit = new(
        "First commit",
        "Commits the generated files, after a git init if the folder isn't in a repository yet.",
        "Committing the generated project",
        null);

    public static readonly SetupStep Bootstrap = new(
        "Bootstrap",
        "Runs " + BootstrapCommand + ": formatting, OpenAPI document, tools and first migrations. Needs internet.",
        "Running bootstrap, this takes a few minutes",
        BootstrapCommand);

    public static readonly SetupStep BootstrapCommit = new(
        "Bootstrap commit",
        "Commits what bootstrap changed, in a commit of its own.",
        "Committing what bootstrap changed",
        null);

    public static readonly SetupStep EnvironmentFile = new(
        "Copy .env",
        "Copies .env.example to .env, which git ignores. The defaults work as they are on your machine.",
        "Copying .env.example to .env",
        EnvironmentCommand);

    public static readonly IReadOnlyList<SetupStep> Steps = [FirstCommit, Bootstrap, BootstrapCommit, EnvironmentFile];

    public static readonly IReadOnlyList<(string Symbol, string Writes)> BootstrapWritesWhen =
    [
        ("scaffoldedAtBootstrap", "creates each project or app a scaffold.json describes"),
        ("dotnetClient", "writes the .NET client"),
        ("anyWeb", "installs the web workspace, which needs Node.js and pnpm"),
    ];

    public static string BootstrapDescription(Func<string, bool> holds) =>
        string.Join(
            ", ",
            [
                BootstrapCommand + ": formats, writes the OpenAPI document, restores the tools, creates the initial migrations",
                .. BootstrapWritesWhen.Where(fact => holds(fact.Symbol)).Select(fact => fact.Writes),
            ])
        + "; needs the network.";

    private const int _tail = 6;
    private const string _init = "git init";
    private const string _longPaths = "git config --global core.longpaths true";

    private static readonly IReadOnlyDictionary<string, string> _noVariables = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly string[] _identity = ["user.name", "user.email"];

    private readonly IProcessRunner _processes;
    private readonly string _root;
    private readonly string _generatedMessage;
    private readonly Action<string>? _echo;

    public ProjectSetup(IProcessRunner processes, string root, string version, Action<string>? echo)
    {
        _processes = processes;
        _root = root;
        _generatedMessage = $"chore: generate from cleanarch-webapi {version}";
        _echo = echo;
    }

    public static IEnumerable<string> Left(IEnumerable<SetupOutcome> outcomes)
    {
        var once = new HashSet<string>(StringComparer.Ordinal);
        foreach (string command in outcomes.SelectMany(outcome => outcome.Rerun))
        {
            bool repeatable = command != _init && !command.StartsWith("git config", StringComparison.Ordinal);
            if (repeatable || once.Add(command))
            {
                yield return command;
            }
        }
    }

    public SetupOutcome Run(SetupStep step, IReadOnlyList<SetupOutcome> before)
    {
        try
        {
            if (step == Bootstrap)
            {
                return Bootstrapped();
            }

            if (step == EnvironmentFile)
            {
                return CopiedEnvironment();
            }

            return step == FirstCommit
                ? Commit(step, _generatedMessage, initialise: true, [])
                : Commit(step, BootstrapMessage, initialise: false, before);
        }
        catch (LauncherException error)
        {
            return new SetupOutcome(step, SetupStatus.Failed, error.Message, Rerun(step));
        }
    }

    internal static string Why(string command, ProcessResult result)
    {
        string[] tail = [.. result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(_tail)];
        return string.Join(Environment.NewLine, tail.Prepend($"{command} exited with code {result.ExitCode}."));
    }

    private static SetupOutcome Failed(SetupStep step, string command, ProcessResult result, IReadOnlyList<string> rerun)
    {
        string why = Why(command, result);
        if (result.Output.Contains("Filename too long", StringComparison.Ordinal))
        {
            why += Environment.NewLine + "Some paths in the project are too long for git on Windows. Turn on long paths, or create the project in a shorter folder.";
            rerun = [_longPaths, .. rerun];
        }

        return new SetupOutcome(step, SetupStatus.Failed, why, rerun);
    }

    private SetupOutcome Commit(SetupStep step, string message, bool initialise, IReadOnlyList<SetupOutcome> before)
    {
        List<string> rerun = Rerun(step);
        if (before.FirstOrDefault(outcome => outcome.Status != SetupStatus.Done) is { } unfinished)
        {
            bool absorbed = unfinished.Step == FirstCommit && before.Any(outcome => outcome.Step == Bootstrap && outcome.Status == SetupStatus.Done);
            return absorbed
                ? new SetupOutcome(step, SetupStatus.Skipped, "Skipped because the first commit didn't go through. Once you make it, it will hold bootstrap's changes too.", [])
                : new SetupOutcome(step, SetupStatus.Skipped, $"Skipped because the {unfinished.Step.Title.ToLowerInvariant()} step didn't go through.", rerun);
        }

        if (!InsideAWorkTree())
        {
            if (!initialise)
            {
                return new SetupOutcome(step, SetupStatus.Skipped, "The project isn't in a git repository.", [_init, .. rerun]);
            }

            if (Git("init") is { ExitCode: not 0 } refused)
            {
                return Failed(step, _init, refused, [_init, .. rerun]);
            }
        }

        if (MissingIdentity() is { Count: > 0 } missing)
        {
            return new SetupOutcome(
                step,
                SetupStatus.Skipped,
                $"git doesn't know who you are yet: {string.Join(" and ", missing)} {(missing.Count == 1 ? "isn't" : "aren't")} set.",
                [.. missing.Select(setting => $"git config --global {setting} \"<your {setting[(setting.IndexOf('.') + 1)..]}>\""), .. rerun]);
        }

        if (Git("add", "-A", "--", ".") is { ExitCode: not 0 } unstaged)
        {
            return Failed(step, "git add", unstaged, rerun);
        }

        if (Git("status", "--porcelain", "--", ".").Output.Trim().Length == 0)
        {
            return new SetupOutcome(step, SetupStatus.Done, "Nothing to commit.", []);
        }

        return Git("commit", "-q", "-m", message, "--", ".") is { ExitCode: not 0 } uncommitted
            ? Failed(step, "git commit", uncommitted, rerun)
            : new SetupOutcome(step, SetupStatus.Done, null, []);
    }

    private SetupOutcome Bootstrapped()
    {
        ProcessResult result = _processes.Run(new ProcessCall("dotnet", _root, ["run", "Tools/Tasks.cs", "bootstrap"], _noVariables), _echo);
        return result.ExitCode == 0
            ? new SetupOutcome(Bootstrap, SetupStatus.Done, null, [])
            : Failed(Bootstrap, BootstrapCommand, result, [BootstrapCommand]);
    }

    private SetupOutcome CopiedEnvironment()
    {
        string example = Path.Combine(_root, ".env.example");
        string environment = Path.Combine(_root, ".env");
        if (File.Exists(environment))
        {
            return new SetupOutcome(EnvironmentFile, SetupStatus.Done, ".env already exists, so it was left as it is.", []);
        }

        if (!File.Exists(example))
        {
            return new SetupOutcome(EnvironmentFile, SetupStatus.Skipped, "There's no .env.example in the project.", []);
        }

        File.Copy(example, environment);
        return new SetupOutcome(EnvironmentFile, SetupStatus.Done, null, []);
    }

    private List<string> Rerun(SetupStep step)
    {
        if (step == Bootstrap)
        {
            return [BootstrapCommand];
        }

        if (step == EnvironmentFile)
        {
            return [EnvironmentCommand];
        }

        return ["git add -A", $"git commit -m \"{(step == FirstCommit ? _generatedMessage : BootstrapMessage)}\""];
    }

    private bool InsideAWorkTree()
    {
        ProcessResult result = Git("rev-parse", "--is-inside-work-tree");
        return result.ExitCode == 0 && result.Output.Trim() == "true";
    }

    private List<string> MissingIdentity() => [.. _identity.Where(setting => !Configured(setting))];

    private bool Configured(string setting)
    {
        ProcessResult result = Git("config", setting);
        return result.ExitCode == 0 && result.Output.Trim().Length > 0;
    }

    private ProcessResult Git(params string[] arguments) =>
        _processes.Run(new ProcessCall("git", _root, arguments, _noVariables), null);
}

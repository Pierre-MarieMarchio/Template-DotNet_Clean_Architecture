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
        "git init, unless the folder is in a git work tree already, then a commit of the generated files.",
        "Committing the generated project",
        null);

    public static readonly SetupStep Bootstrap = new(
        "Bootstrap",
        BootstrapCommand + ": formats, writes the OpenAPI document, restores the tools, creates the initial migrations; needs the network.",
        "Bootstrapping, which takes a few minutes",
        BootstrapCommand);

    public static readonly SetupStep BootstrapCommit = new(
        "Bootstrap commit",
        "A second commit, of what bootstrap wrote, as the first run asks before anything else.",
        "Committing what bootstrap wrote",
        null);

    public static readonly SetupStep EnvironmentFile = new(
        "Copy .env",
        ".env.example to the git-ignored .env, whose defaults work on localhost.",
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

    private static SetupOutcome Failed(SetupStep step, string command, ProcessResult result, IReadOnlyList<string> rerun) =>
        new(step, SetupStatus.Failed, Why(command, result), rerun);

    private SetupOutcome Commit(SetupStep step, string message, bool initialise, IReadOnlyList<SetupOutcome> before)
    {
        List<string> rerun = Rerun(step);
        if (before.FirstOrDefault(outcome => outcome.Status != SetupStatus.Done) is { } unfinished)
        {
            return new SetupOutcome(step, SetupStatus.Skipped, $"{unfinished.Step.Title} did not succeed.", rerun);
        }

        if (!InsideAWorkTree())
        {
            rerun.Insert(0, "git init");
            if (!initialise)
            {
                return new SetupOutcome(step, SetupStatus.Skipped, "The project is not in a git work tree.", rerun);
            }

            if (Git("init") is { ExitCode: not 0 } refused)
            {
                return Failed(step, "git init", refused, rerun);
            }
        }

        if (MissingIdentity() is { Count: > 0 } missing)
        {
            return new SetupOutcome(
                step,
                SetupStatus.Skipped,
                $"git has no {string.Join(" nor ", missing)} to sign the commit with.",
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
            return new SetupOutcome(EnvironmentFile, SetupStatus.Done, ".env is there already.", []);
        }

        if (!File.Exists(example))
        {
            return new SetupOutcome(EnvironmentFile, SetupStatus.Skipped, "The project holds no .env.example.", []);
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

using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

public sealed class ProjectSetup
{
    public static readonly SetupStep FirstCommit = new CommitStep(
        "First commit",
        "Commits the generated files, after a git init if the folder isn't in a repository yet.",
        "Committing the generated project",
        initialises: true);

    public static readonly SetupStep Bootstrap = new BootstrapStep();

    public static readonly SetupStep BootstrapCommit = new CommitStep(
        "Bootstrap commit",
        "Commits what bootstrap changed, in a commit of its own.",
        "Committing what bootstrap changed",
        initialises: false);

    public static readonly SetupStep EnvironmentFile = new EnvironmentFileStep();

    public static readonly IReadOnlyList<SetupStep> Steps = [FirstCommit, Bootstrap, BootstrapCommit, EnvironmentFile];

    private readonly SetupContext _context;

    public ProjectSetup(IProcessRunner processes, string root, string version, Action<string>? echo)
        : this(processes, root, version, echo, Environment.GetEnvironmentVariable)
    {
    }

    public ProjectSetup(IProcessRunner processes, string root, string version, Action<string>? echo, Func<string, string?> environment)
    {
        _context = new SetupContext(processes, root, version, echo, new Git(processes, root, environment));
    }

    public static IEnumerable<string> Left(IEnumerable<SetupOutcome> outcomes)
    {
        var once = new HashSet<string>(StringComparer.Ordinal);
        foreach (string command in outcomes.SelectMany(outcome => outcome.Rerun).Where(command => Git.Repeatable(command) || once.Add(command)))
        {
            yield return command;
        }
    }

    public SetupOutcome Stopped(SetupStep step) => step.Stopped(_context);

    public SetupOutcome Run(SetupStep step, IReadOnlyList<SetupOutcome> before)
    {
        try
        {
            return step.Run(_context, before);
        }
        catch (LauncherException error)
        {
            return new SetupOutcome(step, SetupStatus.Failed, error.Message, step.Rerun(_context));
        }
    }
}

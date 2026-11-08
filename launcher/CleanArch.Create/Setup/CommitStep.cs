namespace CleanArch.Create.Setup;

public sealed class CommitStep(string title, string description, string running, bool initialises)
    : SetupStep(title, description, running, [])
{
    public const string BootstrapMessage = "chore: bootstrap";

    private bool Initialises { get; } = initialises;

    internal override SetupOutcome Run(SetupContext context, IReadOnlyList<SetupOutcome> before)
    {
        List<string> rerun = Rerun(context);
        return Unfinished(Initialises ? [] : before, rerun) ?? Uninitialised(context.Git, rerun) ?? Anonymous(context.Git, rerun) ?? Committed(context, rerun);
    }

    internal override List<string> Rerun(SetupContext context) =>
        ["git add -A", $"git commit -m \"{Message(context)}\""];

    internal override SetupOutcome Stopped(SetupContext context)
    {
        SetupOutcome stopped = base.Stopped(context);
        return context.Git.InsideAWorkTree() ? stopped : stopped with { Rerun = [Git.Init, .. stopped.Rerun] };
    }

    private string Message(SetupContext context) => Initialises ? context.GeneratedMessage : BootstrapMessage;

    private SetupOutcome? Unfinished(IReadOnlyList<SetupOutcome> before, List<string> rerun)
    {
        if (before.FirstOrDefault(outcome => outcome.Status != SetupStatus.Done) is not { } unfinished)
        {
            return null;
        }

        bool absorbed = unfinished.Step is CommitStep { Initialises: true } && before.Any(outcome => outcome.Step is BootstrapStep && outcome.Status == SetupStatus.Done);
        return absorbed
            ? new SetupOutcome(this, SetupStatus.Skipped, "Skipped because the first commit didn't go through. Once you make it, it will hold bootstrap's changes too.", [])
            : new SetupOutcome(this, SetupStatus.Skipped, $"Skipped because the {unfinished.Step.Title.ToLowerInvariant()} step didn't go through.", rerun);
    }

    private SetupOutcome? Uninitialised(Git git, List<string> rerun)
    {
        if (git.InsideAWorkTree())
        {
            return null;
        }

        if (!Initialises)
        {
            return new SetupOutcome(this, SetupStatus.Skipped, "The project isn't in a git repository.", [Git.Init, .. rerun]);
        }

        return git.Run("init") is { ExitCode: not 0 } refused ? Failed(Git.Init, refused, [Git.Init, .. rerun]) : null;
    }

    private SetupOutcome? Anonymous(Git git, List<string> rerun)
    {
        if (git.MissingIdentity() is not { Count: > 0 } missing)
        {
            return null;
        }

        return new SetupOutcome(
            this,
            SetupStatus.Skipped,
            $"git doesn't know who you are yet: {string.Join(" and ", missing)} {(missing.Count == 1 ? "isn't" : "aren't")} set. "
            + "Set them as the commands below say, or through GIT_AUTHOR_NAME, GIT_AUTHOR_EMAIL, GIT_COMMITTER_NAME and GIT_COMMITTER_EMAIL.",
            [.. missing.Select(setting => $"git config --global {setting} \"<your {setting[(setting.IndexOf('.') + 1)..]}>\""), .. rerun]);
    }

    private SetupOutcome Committed(SetupContext context, List<string> rerun)
    {
        Git git = context.Git;
        if (git.Run("add", "-A", "--", ".") is { ExitCode: not 0 } unstaged)
        {
            return Failed("git add", unstaged, rerun);
        }

        if (git.Run("status", "--porcelain", "--", ".").Output.Trim().Length == 0)
        {
            return new SetupOutcome(this, SetupStatus.Done, "Nothing to commit.", []);
        }

        return git.Run("commit", "-q", "-m", Message(context), "--", ".") is { ExitCode: not 0 } uncommitted
            ? Failed("git commit", uncommitted, rerun)
            : new SetupOutcome(this, SetupStatus.Done, null, []);
    }
}

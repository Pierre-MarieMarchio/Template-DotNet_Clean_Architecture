using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

public abstract class SetupStep(string title, string description, string running, IReadOnlyList<string> replaces)
{
    public string Title { get; } = title;

    public string Description { get; } = description;

    public string Running { get; } = running;

    public IReadOnlyList<string> Replaces { get; } = replaces;

    internal abstract SetupOutcome Run(SetupContext context, IReadOnlyList<SetupOutcome> before);

    internal abstract List<string> Rerun(SetupContext context);

    internal virtual SetupOutcome Stopped(SetupContext context) =>
        new(this, SetupStatus.Skipped, "Skipped because the setup was stopped.", Rerun(context));

    private protected SetupOutcome Failed(string command, ProcessResult result, IReadOnlyList<string> rerun, string? journal = null)
    {
        string why = ProcessFailure.Why(command, result, journal);
        if (result.Output.Contains("Filename too long", StringComparison.Ordinal))
        {
            why += Environment.NewLine + "Some paths in the project are too long for git on Windows. Turn on long paths, or create the project in a shorter folder.";
            rerun = [Git.LongPaths, .. rerun];
        }

        return new SetupOutcome(this, SetupStatus.Failed, why, rerun);
    }
}

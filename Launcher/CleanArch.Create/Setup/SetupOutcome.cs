namespace CleanArch.Create.Setup;

public enum SetupStatus
{
    Done,
    Failed,
    Skipped,
}

public sealed record SetupOutcome(SetupStep Step, SetupStatus Status, string? Why, IReadOnlyList<string> Rerun)
{
    public string Text => Step.Title + " " + Status.ToString().ToLowerInvariant();
}

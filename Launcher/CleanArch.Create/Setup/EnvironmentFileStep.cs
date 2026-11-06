namespace CleanArch.Create.Setup;

public sealed class EnvironmentFileStep()
    : SetupStep(
        "Copy .env",
        "Copies .env.example to .env, which git ignores. The defaults work as they are on your machine.",
        "Copying .env.example to .env",
        Command)
{
    public const string Command = "cp .env.example .env";

    internal override SetupOutcome Run(SetupContext context, IReadOnlyList<SetupOutcome> before)
    {
        string example = Path.Combine(context.Root, ".env.example");
        string environment = Path.Combine(context.Root, ".env");
        if (File.Exists(environment))
        {
            return new SetupOutcome(this, SetupStatus.Done, ".env already exists, so it was left as it is.", []);
        }

        if (!File.Exists(example))
        {
            return new SetupOutcome(this, SetupStatus.Skipped, "There's no .env.example in the project.", []);
        }

        File.Copy(example, environment);
        return new SetupOutcome(this, SetupStatus.Done, null, []);
    }

    internal override List<string> Rerun(SetupContext context) => [Command];
}

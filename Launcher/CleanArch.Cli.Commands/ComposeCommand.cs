using System.CommandLine;

namespace CleanArch.Cli;

internal static class ComposeCommand
{
    public const string Name = "compose";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var compose = new Command(Name, "Start or stop this project's Compose stack, as docker compose does.");
        compose.Add(UpCommand(context));
        compose.Add(DownCommand(context));
        compose.SetAction(parseResult =>
        {
            parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
            return CleanArchCommand.Succeeded;
        });
        return compose;
    }

    private static Command UpCommand(CommandContext context)
    {
        var up = new Command("up", "Start the stack in the background and wait until every service is healthy.");
        if (context.Project is { } project)
        {
            up.SetAction(parseResult => ExternalCommand.Run(
                new ExternalCall("docker", ["compose", "up", "-d", "--wait"], project.Root),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error));
        }

        return up;
    }

    private static Command DownCommand(CommandContext context)
    {
        var down = new Command("down", "Stop the stack and remove its containers.");
        if (context.Project is { } project)
        {
            down.SetAction(parseResult => ExternalCommand.Run(
                new ExternalCall("docker", ["compose", "down"], project.Root),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error));
        }

        return down;
    }
}

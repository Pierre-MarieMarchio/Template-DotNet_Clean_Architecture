using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Compose;

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
            up.SetAction(parseResult => context.Shell(parseResult).Run(new ExternalCall("docker", ["compose", "up", "-d", "--wait"], project.Root)));
        }

        return up;
    }

    private static Command DownCommand(CommandContext context)
    {
        var down = new Command("down", "Stop the stack and remove its containers.");
        if (context.Project is { } project)
        {
            down.SetAction(parseResult => context.Shell(parseResult).Run(new ExternalCall("docker", ["compose", "down"], project.Root)));
        }

        return down;
    }
}

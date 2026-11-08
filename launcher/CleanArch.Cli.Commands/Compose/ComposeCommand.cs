using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Compose;

internal static class ComposeCommand
{
    public const string Name = "compose";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context) =>
        CommandGroup.Of(
            Name,
            "Start or stop this project's Compose stack, as docker compose does.",
            Subcommand(context, "up", "Start the stack in the background and wait until every service is healthy.", "up", "-d", "--wait"),
            Subcommand(context, "down", "Stop the stack and remove its containers.", "down"));

    private static Command Subcommand(CommandContext context, string name, string description, params string[] arguments)
    {
        var subcommand = new Command(name, description);
        if (context.Project is { } project)
        {
            subcommand.SetAction(parseResult => context.Shell(parseResult).Run(Docker.Compose(project.Root, arguments)));
        }

        return subcommand;
    }
}

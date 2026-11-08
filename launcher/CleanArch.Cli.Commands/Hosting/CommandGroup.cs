using System.CommandLine;

namespace CleanArch.Cli.Commands.Hosting;

internal static class CommandGroup
{
    public static Command Of(string name, string description, params IEnumerable<Command> subcommands)
    {
        var group = new Command(name, description);
        foreach (Command subcommand in subcommands)
        {
            group.Add(subcommand);
        }

        group.SetAction(parseResult =>
        {
            parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
            return CleanArchCommand.Succeeded;
        });
        return group;
    }
}

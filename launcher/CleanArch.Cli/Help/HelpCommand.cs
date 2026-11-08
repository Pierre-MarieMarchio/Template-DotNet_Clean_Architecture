using System.CommandLine;

namespace CleanArch.Cli.Help;

internal static class HelpCommand
{
    public const string Name = "help";

    public static readonly CommandRegistration Registration = new(
        Refusal: _ => null,
        Build: Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Show what this project's commands are.");
        Option<bool> json = new("--json") { Description = "List the commands and options that apply to this project, as JSON." };
        command.Options.Add(json);
        command.SetAction(parseResult => Run(context, parseResult, json));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, Option<bool> json)
    {
        if (parseResult.GetValue(json))
        {
            return HelpJson.Write(context, parseResult);
        }

        parseResult.RootCommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
        return CleanArchCommand.Succeeded;
    }
}

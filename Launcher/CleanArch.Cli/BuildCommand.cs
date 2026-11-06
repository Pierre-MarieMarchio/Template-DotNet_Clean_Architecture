using System.CommandLine;

namespace CleanArch.Cli;

internal static class BuildCommand
{
    public const string Name = "build";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Build this project's solution.");
        BuildSettings.Options settings = BuildSettings.Add(command);
        command.SetAction(parseResult => Run(context, parseResult, settings.Of(parseResult)));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, BuildSettings settings)
    {
        if (context.Project is not { } project)
        {
            return CleanArchCommand.Failed;
        }

        return Dotnet.Run(parseResult, project.Root, ["build", ProjectLayout.Solution(project), "--configuration", settings.Configuration, .. settings.Nodes]);
    }
}

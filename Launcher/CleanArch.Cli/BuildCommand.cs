using System.CommandLine;

namespace CleanArch.Cli;

internal static class BuildCommand
{
    public const string Name = "build";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Build this project's solution.");
        command.SetAction(parseResult => Run(context, parseResult));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult)
    {
        if (context.Project is not { } project)
        {
            return CleanArchCommand.Failed;
        }

        string solution = Path.Combine(project.Root, ProjectLayout.Solution(project));
        return Dotnet.Run(parseResult, project.Root, ["build", SolutionLocator.Relative(project.Root, solution), "--configuration", "Debug"]);
    }
}

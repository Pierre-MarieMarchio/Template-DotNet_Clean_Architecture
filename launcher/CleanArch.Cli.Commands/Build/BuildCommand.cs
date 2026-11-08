using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Build;

internal static class BuildCommand
{
    public const string Name = "build";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Build this project's solution.");
        BuildSettings.Options settings = BuildSettings.Add(command);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), settings.Of(parseResult)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings) =>
        shell.Run(Dotnet.Build(project.Root, ProjectLayout.Solution(project), settings.Configuration, settings.Nodes));
}

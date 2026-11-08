using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Run;

internal static class RunCommand
{
    public const string Name = "run";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Requiring(project => Refusals.WithoutApi(project, Name)), Create);

    private static Command Create(CommandContext context)
    {
        var run = new Command(Name, "Run the API, as dotnet run does.");
        if (context.Project is { } project)
        {
            run.SetAction(parseResult => context.Shell(parseResult).Run(Dotnet.Call(project.Root, "run", "--project", ProjectLayout.Api(project))));
        }

        return run;
    }
}

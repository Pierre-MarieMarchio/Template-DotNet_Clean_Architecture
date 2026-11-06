using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Run;

internal static class RunCommand
{
    public const string Name = "run";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static string? Refusal(CommandContext context) =>
        ProjectCommand.Refusal(context)
        ?? (context.Project is { } project && !ProjectLayout.HoldsTheApi(project)
            ? $"This project has no run command: its {ProjectLocator.FileName} records no presentation of framework api."
            : null);

    private static Command Build(CommandContext context)
    {
        var run = new Command(Name, "Run the API, as dotnet run does.");
        if (context.Project is { } project && ProjectLayout.HoldsTheApi(project))
        {
            run.SetAction(parseResult => ExternalCommand.Run(
                new ExternalCall("dotnet", ["run", "--project", ProjectLayout.Api(project)], project.Root),
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error));
        }

        return run;
    }
}

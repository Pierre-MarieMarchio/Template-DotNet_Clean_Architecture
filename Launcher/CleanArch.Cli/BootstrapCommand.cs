using System.CommandLine;

namespace CleanArch.Cli;

internal static class BootstrapCommand
{
    public const string Name = "bootstrap";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private const string _initialMigrationName = "InitialCreate";

    private static Command Build(CommandContext context)
    {
        var bootstrap = new Command(Name, "Format the solution, restore the local tools and create each context's initial migration. Commit what it writes before anything else.");
        if (context.Project is { } project)
        {
            bootstrap.SetAction(parseResult => Run(project, parseResult));
        }

        return bootstrap;
    }

    private static int Run(LocatedProject project, ParseResult parseResult)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;

        int exit = ExternalCommand.Run(
            new ExternalCall("dotnet", ["format", ProjectLayout.SolutionName(project), "--diagnostics", "IDE0055", "IDE0005"], project.Root),
            output,
            error);
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        exit = ExternalCommand.Run(new ExternalCall("dotnet", ["tool", "restore"], project.Root), output, error);
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        foreach (string context in ProjectLayout.Contexts(project))
        {
            if (HasMigrations(project, context))
            {
                output.WriteLine($"{Path.GetFileName(context)} already has its migrations; they are left as they are.");
                continue;
            }

            exit = ExternalCommand.Run(
                new ExternalCall(
                    "dotnet",
                    ["ef", "migrations", "add", _initialMigrationName, "--project", context, "--startup-project", context],
                    project.Root),
                output,
                error);
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }

    private static bool HasMigrations(LocatedProject project, string context)
    {
        string folder = Path.Combine(project.Root, context, "Migrations");
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
    }
}

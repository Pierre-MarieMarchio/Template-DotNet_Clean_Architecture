using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;
using CleanArch.Cli.Commands.OpenApi;

namespace CleanArch.Cli.Commands.Bootstrap;

internal static class BootstrapCommand
{
    public const string Name = "bootstrap";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private const string _initialMigrationName = "InitialCreate";

    private sealed record Step(string Done, Func<int> Run, string? IfNotDone = null);

    private static Command Build(CommandContext context)
    {
        var bootstrap = new Command(
            Name,
            "Set up the project once, right after generating it: scaffold its pages, format the solution, write the documentation index and the generated files, "
            + "restore the local tools and create each context's initial migration. Commit what it writes before anything else.");
        if (context.Project is { } project)
        {
            bootstrap.SetAction(parseResult => Run(project, context.Shell(parseResult)));
        }

        return bootstrap;
    }

    private static int Run(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        Step[] steps = [.. Steps(project, shell)];

        for (int index = 0; index < steps.Length; index++)
        {
            int exit = steps[index].Run();
            if (exit != CleanArchCommand.Succeeded)
            {
                error.WriteLine(
                    $"{Name} stopped at {steps[index].Done}. "
                    + $"Done: {Listed(steps[..index])}. Not done: {Listed(steps[index..])}. "
                    + (steps[index].IfNotDone is { } consequence ? consequence + " " : string.Empty)
                    + $"What is done stays done: fix the cause and run `{CleanArchCommand.Name} {Name}` again.");
                return exit;
            }
        }

        output.WriteLine($"Done: {Listed(steps)}. Commit this before anything else: the migrations are the project's own from now on.");
        return CleanArchCommand.Succeeded;
    }

    private static IEnumerable<Step> Steps(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        int Dotnet(params string[] arguments) => shell.Run(new ExternalCall("dotnet", arguments, project.Root));

        yield return new("the presentation projects' pages", () => Dotnet("run", ProjectLayout.Tasks, "presentation-scaffold"));
        yield return new("the formatting", () => Dotnet("format", ProjectLayout.Solution(project), "--diagnostics", "IDE0055", "IDE0005"));
        yield return new("the documentation index", () => Dotnet("run", ProjectLayout.Tasks, "docs-index"));

        bool holdsTheApi = ProjectLayout.HoldsTheApi(project);
        if (holdsTheApi)
        {
            yield return new("the OpenAPI reference", () => OpenApiCommand.WriteReference(project, checking: false, noBuild: false, shell));
        }

        yield return new(
            "the local tools' restore",
            () => Dotnet("tool", "restore"),
            "The local tools are not restored, so the initial migrations are not created and the commands that run dotnet ef cannot work yet: "
            + $"{CleanArchCommand.Name} migrations add, database update, migrations bundle and verify.");
        yield return new("each context's initial migration", () => InitialMigrations(project, shell));

        if (holdsTheApi && project.File.Client)
        {
            yield return new("the .NET client", () => OpenApiCommand.RegenerateClient(project, checking: false, shell));
        }

        if (ProjectLayout.HasWebApps(project))
        {
            yield return new("the web apps' pages", () => Dotnet("run", ProjectLayout.Tasks, "web-scaffold"));
        }
    }

    private static int InitialMigrations(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        foreach (string context in ProjectLayout.Contexts(project))
        {
            if (PersistenceContexts.HasInitialMigration(project, context))
            {
                output.WriteLine($"{Path.GetFileName(context)} already has its migrations; they are left as they are.");
                continue;
            }

            int exit = shell.Run(new ExternalCall(
                    "dotnet",
                    ["ef", "migrations", "add", _initialMigrationName, "--project", context, "--startup-project", context],
                    project.Root));
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }

    private static string Listed(Step[] steps) => steps.Length switch
    {
        0 => "nothing yet",
        1 => steps[0].Done,
        _ => string.Join(", ", steps[..^1].Select(step => step.Done)) + " and " + steps[^1].Done,
    };
}

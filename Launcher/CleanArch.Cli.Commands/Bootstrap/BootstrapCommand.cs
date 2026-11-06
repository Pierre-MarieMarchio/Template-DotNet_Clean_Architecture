using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Bootstrap;

internal static class BootstrapCommand
{
    public const string Name = "bootstrap";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private const string _initialMigrationName = "InitialCreate";

    private sealed record Step(string Done, Func<int> Run, string? IfNotDone = null);

    private static Command Create(CommandContext context)
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
        Step[] steps = [.. Steps(project, shell)];
        for (int index = 0; index < steps.Length; index++)
        {
            int exit = steps[index].Run();
            if (exit != ExitCode.Succeeded)
            {
                shell.Error.WriteLine(
                    $"{Name} stopped at {steps[index].Done}. "
                    + $"Done: {Listed(steps[..index])}. Not done: {Listed(steps[index..])}. "
                    + (steps[index].IfNotDone is { } consequence ? consequence + " " : string.Empty)
                    + $"What is done stays done: fix the cause and run `{Tool.Name} {Name}` again.");
                return exit;
            }
        }

        shell.Output.WriteLine($"Done: {Listed(steps)}. Commit this before anything else: the migrations are the project's own from now on.");
        return ExitCode.Succeeded;
    }

    private static IEnumerable<Step> Steps(LocatedProject project, Shell shell)
    {
        string root = project.Root;
        bool holdsTheApi = ProjectLayout.HoldsTheApi(project);
        return
        [
            new("the presentation projects' pages", () => shell.Run(Dotnet.RunFile(root, ProjectLayout.Tasks, "presentation-scaffold"))),
            new("the formatting", () => shell.Run(Dotnet.Call(root, "format", ProjectLayout.Solution(project), "--diagnostics", "IDE0055", "IDE0005"))),
            new("the documentation index", () => shell.Run(Dotnet.RunFile(root, ProjectLayout.Tasks, "docs-index"))),
            .. holdsTheApi ? [new Step("the OpenAPI reference", () => OpenApiReference.Write(project, shell, noBuild: false))] : Array.Empty<Step>(),
            new(
                "the local tools' restore",
                () => shell.Run(Dotnet.Call(root, "tool", "restore")),
                "The local tools are not restored, so the initial migrations are not created and the commands that run dotnet ef cannot work yet: "
                + $"{Tool.Name} migrations add, database update, migrations bundle and verify."),
            new("each context's initial migration", () => ExitCode.FirstFailureOf(ProjectLayout.Contexts(project).Select(context => (Func<int>)(() => InitialMigration(project, shell, context))))),
            .. holdsTheApi && project.File.Client ? [new Step("the .NET client", () => ApiClient.Write(project, shell))] : Array.Empty<Step>(),
            .. ProjectLayout.HasWebApps(project) ? [new Step("the web apps' pages", () => shell.Run(Dotnet.RunFile(root, ProjectLayout.Tasks, "web-scaffold")))] : Array.Empty<Step>(),
        ];
    }

    private static int InitialMigration(LocatedProject project, Shell shell, string context)
    {
        if (!PersistenceContexts.HasInitialMigration(project, context))
        {
            return shell.Run(Dotnet.Ef(project.Root, context, ["migrations", "add", _initialMigrationName]));
        }

        shell.Output.WriteLine($"{Path.GetFileName(context)} already has its migrations; they are left as they are.");
        return ExitCode.Succeeded;
    }

    private static string Listed(Step[] steps) => steps.Length switch
    {
        0 => "nothing yet",
        1 => steps[0].Done,
        _ => string.Join(", ", steps[..^1].Select(step => step.Done)) + " and " + steps[^1].Done,
    };
}

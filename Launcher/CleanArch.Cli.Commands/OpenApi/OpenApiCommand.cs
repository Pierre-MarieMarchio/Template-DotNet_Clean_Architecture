using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.OpenApi;

internal static class OpenApiCommand
{
    public const string Name = "openapi";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Requiring(Refusal), Create);

    private static string? Refusal(LocatedProject project) =>
        ProjectLayout.HoldsTheApi(project)
            ? null
            : $"This project has no {Name} command: its {ProjectLocator.FileName} records no api presentation, so it holds no HTTP API to describe.";

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Write the OpenAPI reference the API describes itself with, then regenerate every client from it: the .NET client where the project holds one, the web workspace's schema where it holds the api-client package.");
        Option<bool> check = new("--check")
        {
            Description = "Fail where the command would write, instead of writing the OpenAPI reference and what is generated from it.",
        };
        Option<bool> noBuild = new("--no-build")
        {
            Description = "Write the reference from the API's last build instead of building it again.",
        };
        command.Options.Add(check);
        command.Options.Add(noBuild);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult.GetValue(check), parseResult.GetValue(noBuild)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, bool checking, bool noBuild) =>
        ExitCode.FirstFailureOf(checking ? Checks(project, shell, noBuild) : Writes(project, shell, noBuild));

    private static IEnumerable<Func<int>> Writes(LocatedProject project, Shell shell, bool noBuild) =>
    [
        () => OpenApiReference.Write(project, shell, noBuild),
        .. project.File.Client ? [() => ApiClient.Write(project, shell)] : Array.Empty<Func<int>>(),
        .. WebSchema.IsGenerated(project) ? [() => shell.Run(Pnpm.In(project, ProjectLayout.Web, "api", OpenApiReference.PathOf(project)))] : Array.Empty<Func<int>>(),
    ];

    private static IEnumerable<Func<int>> Checks(LocatedProject project, Shell shell, bool noBuild) =>
    [
        () => OpenApiReference.Check(project, shell, noBuild),
        .. project.File.Client ? [() => CheckClient(project, shell)] : Array.Empty<Func<int>>(),
        .. WebSchema.IsGenerated(project) ? [() => shell.Run(WebSchema.Check(project, OpenApiReference.PathOf(project)))] : Array.Empty<Func<int>>(),
    ];

    private static int CheckClient(LocatedProject project, Shell shell)
    {
        int exit = ApiClient.Check(project, shell, OpenApiReference.PathOf(project), "cleanarch-client-", "write");
        if (exit == ExitCode.Succeeded)
        {
            shell.Output.WriteLine($"{ProjectPath.Relative(project.Root, ApiClient.Generated(project))} is what Kiota writes from the OpenAPI reference.");
        }

        return exit;
    }
}

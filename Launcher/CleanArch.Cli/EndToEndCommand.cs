using System.CommandLine;

namespace CleanArch.Cli;

internal static class EndToEndCommand
{
    public const string Name = "end-to-end";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static string? Refusal(CommandContext context) =>
        ProjectCommand.Refusal(context)
        ?? (context.Project is { } project && !ProjectLayout.HoldsTheApi(project)
            ? $"This project has no end-to-end command: its {ProjectLocator.FileName} records no presentation of framework api."
            : null);

    private static Command Build(CommandContext context)
    {
        var endToEnd = new Command(Name, "Start the Compose stack from .env.example under a project name of its own, run the end-to-end tests against it, then remove the stack and its volumes.");
        Option<bool> keepStack = new("--keep-stack")
        {
            Description = "Leave the stack running, data included, for another run or a look at its logs.",
        };
        endToEnd.Options.Add(keepStack);
        if (context.Project is { } project && ProjectLayout.HoldsTheApi(project))
        {
            endToEnd.SetAction(parseResult => Run(project, parseResult, keepStack));
        }

        return endToEnd;
    }

    private static int Run(LocatedProject project, ParseResult parseResult, Option<bool> keepStack)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        string[] compose = ["compose", "--project-name", ProjectLayout.EndToEndComposeProject(project), "--env-file", ".env.example"];

        int exit = ExternalCommand.Run(
            new ExternalCall("docker", [.. compose, "up", "--build", "--detach", "--wait", "--wait-timeout", "300"], project.Root),
            output,
            error);
        if (exit == CleanArchCommand.Succeeded)
        {
            exit = ExternalCommand.Run(
                new ExternalCall(
                    "dotnet",
                    ["run", "--project", ProjectLayout.EndToEndTests(project), "--configuration", "Debug"],
                    project.Root),
                output,
                error);
        }

        if (exit == CleanArchCommand.Succeeded && ProjectLayout.HasWebApps(project))
        {
            exit = ExternalCommand.Run(
                new ExternalCall(
                    OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm",
                    ["--filter", "*browser-smoke", "smoke"],
                    Path.Combine(project.Root, "Src", "Web")),
                output,
                error);
        }

        if (exit != CleanArchCommand.Succeeded)
        {
            ExternalCommand.Run(new ExternalCall("docker", [.. compose, "logs", "--tail", "100"], project.Root), output, error);
        }

        if (!parseResult.GetValue(keepStack))
        {
            int down = ExternalCommand.Run(new ExternalCall("docker", [.. compose, "down", "--volumes"], project.Root), output, error);
            if (exit == CleanArchCommand.Succeeded)
            {
                exit = down;
            }
        }

        return exit;
    }
}

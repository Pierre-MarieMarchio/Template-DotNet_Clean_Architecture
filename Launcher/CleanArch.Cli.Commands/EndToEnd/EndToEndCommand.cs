using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.EndToEnd;

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
        Option<string> configuration = BuildSettings.AddConfiguration(endToEnd);
        if (context.Project is { } project && ProjectLayout.HoldsTheApi(project))
        {
            endToEnd.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult, keepStack, configuration));
        }

        return endToEnd;
    }

    private static int Run(LocatedProject project, Shell shell, ParseResult parseResult, Option<bool> keepStack, Option<string> configuration)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string[] compose = ["compose", "--project-name", ProjectLayout.EndToEndComposeProject(project), "--env-file", ".env.example"];
        IReadOnlyDictionary<string, string> environment = Unset(Defaults(project));
        int exit = CleanArchCommand.Failed;

        try
        {
            exit = shell.Run(new ExternalCall("docker", [.. compose, "up", "--build", "--detach", "--wait", "--wait-timeout", "300"], project.Root, environment));
            if (exit == CleanArchCommand.Succeeded)
            {
                exit = shell.Run(new ExternalCall(
                        "dotnet",
                        ["run", "--project", ProjectLayout.EndToEndTests(project), "--configuration", parseResult.GetRequiredValue(configuration)],
                        project.Root,
                        environment));
            }

            if (exit == CleanArchCommand.Succeeded && ProjectLayout.HasWebApps(project))
            {
                exit = BrowserSmoke(project, environment, shell);
            }
        }
        finally
        {
            if (exit != CleanArchCommand.Succeeded)
            {
                shell.Run(new ExternalCall("docker", [.. compose, "logs", "--tail", "100"], project.Root, environment));
            }

            if (!parseResult.GetValue(keepStack))
            {
                int down = shell.Run(new ExternalCall("docker", [.. compose, "down", "--volumes"], project.Root, environment));
                if (exit == CleanArchCommand.Succeeded)
                {
                    exit = down;
                }
            }
        }

        return exit;
    }

    private static int BrowserSmoke(LocatedProject project, IReadOnlyDictionary<string, string> environment, Shell shell)
    {
        TextWriter error = shell.Error;
        var smoke = new Dictionary<string, string>(environment, StringComparer.Ordinal) { ["SMOKE_ENV_FILE"] = ".env.example" };
        int exit = shell.Run(new ExternalCall(
                OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm",
                ["--filter", "*browser-smoke", "smoke"],
                Path.Combine(project.Root, "Src", "Web"),
                smoke));
        if (exit != CleanArchCommand.Succeeded)
        {
            error.WriteLine(
                "The browser smoke needs `pnpm install --frozen-lockfile` in Src/Web and Chromium: install it with "
                + "`pnpm --filter *browser-smoke exec playwright install --only-shell chromium` in Src/Web, adding `--with-deps` on Linux "
                + "for the system libraries it loads.");
        }

        return exit;
    }

    private static Dictionary<string, string> Defaults(LocatedProject project)
    {
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        if (project.File.Options.Auth == "external")
        {
            defaults["EXTERNAL_AUTH_AUTHORITY"] = "https://identity-provider.invalid/";
            defaults["EXTERNAL_AUTH_AUDIENCE"] = "end-to-end";
            if (ProjectLayout.RendersAWebAppOnTheServer(project))
            {
                defaults["EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID"] = "end-to-end";
            }

            if (ProjectLayout.HasAWebAppOnWebAssembly(project))
            {
                defaults["EXTERNAL_AUTH_BROWSER_CLIENT_ID"] = "end-to-end";
            }
        }

        if (project.File.Options.Files == true)
        {
            defaults["JOBS_FILES_INSPECT_INTERVAL"] = "00:00:10";
        }

        return defaults;
    }

    private static Dictionary<string, string> Unset(Dictionary<string, string> defaults) =>
        defaults
            .Where(variable => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable.Key)))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);
}

using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.EndToEnd;

internal static class EndToEndCommand
{
    public const string Name = "end-to-end";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Requiring(project => Refusals.WithoutApi(project, Name)), Create);

    private sealed record Stack(LocatedProject Project, IReadOnlyDictionary<string, string> Environment)
    {
        public ExternalCall Compose(params IReadOnlyList<string> arguments)
        {
            ExternalCall compose = Docker.Compose(Project.Root, ["--project-name", ProjectLayout.EndToEndComposeProject(Project), "--env-file", ".env.example", .. arguments]);
            return compose with { Environment = Environment };
        }
    }

    private static Command Create(CommandContext context)
    {
        var endToEnd = new Command(Name, "Start the Compose stack from .env.example under a project name of its own, run the end-to-end tests against it, then remove the stack and its volumes.");
        Option<bool> keepStack = new("--keep-stack")
        {
            Description = "Leave the stack running, data included, for another run or a look at its logs.",
        };
        endToEnd.Options.Add(keepStack);
        Option<string> configuration = BuildSettings.AddConfiguration(endToEnd);
        if (context.Project is { } project)
        {
            endToEnd.SetAction(parseResult => Run(
                new Stack(project, Unset(Defaults(project))),
                context.Shell(parseResult),
                parseResult.GetValue(keepStack),
                parseResult.GetRequiredValue(configuration)));
        }

        return endToEnd;
    }

    private static int Run(Stack stack, Shell shell, bool keepStack, string configuration)
    {
        int exit = ExitCode.Failed;
        try
        {
            exit = ExitCode.FirstFailureOf(
            [
                () => shell.Run(stack.Compose("up", "--build", "--detach", "--wait", "--wait-timeout", "300")),
                () => shell.Run(Dotnet.Call(stack.Project.Root, "run", "--project", ProjectLayout.EndToEndTests(stack.Project), "--configuration", configuration) with { Environment = stack.Environment }),
                .. ProjectLayout.HasWebApps(stack.Project) ? [() => BrowserSmoke(stack, shell)] : Array.Empty<Func<int>>(),
            ]);
        }
        finally
        {
            exit = TearDown(stack, shell, keepStack, exit);
        }

        return exit;
    }

    private static int TearDown(Stack stack, Shell shell, bool keepStack, int exit)
    {
        if (exit != ExitCode.Succeeded)
        {
            shell.Run(stack.Compose("logs", "--tail", "100"));
        }

        if (keepStack)
        {
            return exit;
        }

        int down = shell.Run(stack.Compose("down", "--volumes"));
        return exit == ExitCode.Succeeded ? down : exit;
    }

    private static int BrowserSmoke(Stack stack, Shell shell)
    {
        var smoke = new Dictionary<string, string>(stack.Environment, StringComparer.Ordinal) { ["SMOKE_ENV_FILE"] = ".env.example" };
        string web = ProjectPath.Shown(ProjectLayout.Web);
        int exit = shell.Run(Pnpm.Call(Path.Combine(stack.Project.Root, ProjectLayout.Web), "--filter", "*browser-smoke", "smoke") with { Environment = smoke });
        if (exit != ExitCode.Succeeded)
        {
            shell.Error.WriteLine(
                $"The browser smoke needs `pnpm install --frozen-lockfile` in {web} and Chromium: install it with "
                + $"`pnpm --filter *browser-smoke exec playwright install --only-shell chromium` in {web}, adding `--with-deps` on Linux "
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

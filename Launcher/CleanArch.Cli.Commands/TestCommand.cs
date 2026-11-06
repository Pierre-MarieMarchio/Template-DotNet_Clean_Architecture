using System.CommandLine;

namespace CleanArch.Cli;

internal static class TestCommand
{
    public const string Name = "test";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Run this project's tests.");
        Option<bool> noIntegration = new("--no-integration")
        {
            Description = "Build every test project, and run only the ones that set IsTestProject and reference neither "
                + "Testcontainers nor Aspire.Hosting.Testing, so no Docker daemon is needed; it names each project it built and did not run.",
        };
        command.Options.Add(noIntegration);
        BuildSettings.Options settings = BuildSettings.Add(command);
        command.SetAction(parseResult => Run(context, parseResult, parseResult.GetValue(noIntegration), settings.Of(parseResult)));
        return command;
    }

    public static int RunBuilt(LocatedProject project, ParseResult parseResult, BuildSettings settings) =>
        Guarded(parseResult, () =>
        {
            string root = project.Root;
            string solution = Path.Combine(root, ProjectLayout.Solution(project));
            string[] skipped = Skipped(root, parseResult);
            string? run = skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
            try
            {
                return Dotnet.Run(parseResult, root, ["test", "--solution", SolutionLocator.Relative(root, run ?? solution), "--configuration", settings.Configuration, "--no-build", .. settings.Modules]);
            }
            finally
            {
                SolutionFilter.Delete(run);
            }
        });

    private static int Run(CommandContext context, ParseResult parseResult, bool dockerFreeOnly, BuildSettings settings)
    {
        if (context.Project is not { } project)
        {
            return CleanArchCommand.Failed;
        }

        return Guarded(parseResult, () =>
        {
            string root = project.Root;
            string solution = Path.Combine(root, ProjectLayout.Solution(project));
            TextWriter output = parseResult.InvocationConfiguration.Output;
            string[] tests = TestProjects.All(root);
            string[] dockerFree = [.. tests.Where(path => TestProjects.IsDockerFree(File.ReadAllText(path)))];
            string[] notRun = dockerFreeOnly ? [.. tests.Except(dockerFree, StringComparer.Ordinal)] : [];
            foreach (string test in notRun)
            {
                output.WriteLine(TestProjects.NotRunLine(root, test));
            }

            string[] skipped = Skipped(root, parseResult);
            string? run = dockerFreeOnly
                ? SolutionFilter.Write(root, solution, "docker-free.slnf", dockerFree.Except(skipped, StringComparer.Ordinal))
                : skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
            string? build = dockerFreeOnly && (skipped.Length > 0 || notRun.Length > 0)
                ? SolutionFilter.Write(root, solution, "docker-free-build.slnf", tests)
                : null;
            try
            {
                string target = SolutionLocator.Relative(root, run ?? solution);
                if (settings.MaxCpuCount is null && skipped.Length == 0 && notRun.Length == 0)
                {
                    return Dotnet.Run(parseResult, root, ["test", "--solution", target, "--configuration", settings.Configuration]);
                }

                int exit = Dotnet.Run(parseResult, root, ["build", SolutionLocator.Relative(root, build ?? run ?? solution), "--configuration", settings.Configuration, .. settings.Nodes]);
                return exit != CleanArchCommand.Succeeded
                    ? exit
                    : Dotnet.Run(parseResult, root, ["test", "--solution", target, "--configuration", settings.Configuration, "--no-build", .. settings.Modules]);
            }
            finally
            {
                SolutionFilter.Delete(run);
                SolutionFilter.Delete(build);
            }
        });
    }

    private static string[] Skipped(string root, ParseResult parseResult)
    {
        string[] skipped = TestProjects.SkippedHere(TestProjects.All(root));
        foreach (string test in skipped)
        {
            parseResult.InvocationConfiguration.Output.WriteLine(TestProjects.SkipLine(root, test));
        }

        return skipped;
    }

    private static int Guarded(ParseResult parseResult, Func<int> run)
    {
        try
        {
            return run();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            parseResult.InvocationConfiguration.Error.WriteLine(error.Message);
            return CleanArchCommand.Failed;
        }
    }
}

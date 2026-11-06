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
        command.SetAction(parseResult => Run(context, parseResult, noIntegration));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, Option<bool> noIntegration)
    {
        if (context.Project is not { } project)
        {
            return CleanArchCommand.Failed;
        }

        string root = project.Root;
        string solution = SolutionLocator.Find(project);
        TextWriter output = parseResult.InvocationConfiguration.Output;
        bool dockerFreeOnly = parseResult.GetValue(noIntegration);
        try
        {
            string[] tests = TestProjects.All(root);
            string[] dockerFree = [.. tests.Where(path => TestProjects.IsDockerFree(File.ReadAllText(path)))];
            string[] skipped = TestProjects.SkippedHere(tests);
            string[] notRun = dockerFreeOnly ? [.. tests.Except(dockerFree, StringComparer.Ordinal)] : [];

            foreach (string test in notRun)
            {
                output.WriteLine(TestProjects.NotRunLine(root, test));
            }

            foreach (string test in skipped)
            {
                output.WriteLine(TestProjects.SkipLine(root, test));
            }

            string? run = dockerFreeOnly
                ? SolutionFilter.Write(root, solution, "docker-free.slnf", dockerFree.Except(skipped, StringComparer.Ordinal))
                : skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
            string? build = dockerFreeOnly && (skipped.Length > 0 || notRun.Length > 0)
                ? SolutionFilter.Write(root, solution, "docker-free-build.slnf", tests)
                : null;
            try
            {
                if (skipped.Length == 0 && notRun.Length == 0)
                {
                    return Dotnet.Run(parseResult, root, ["test", "--solution", SolutionLocator.Relative(root, run ?? solution), "--configuration", "Debug"]);
                }

                int exit = Dotnet.Run(parseResult, root, ["build", SolutionLocator.Relative(root, build ?? run ?? solution), "--configuration", "Debug"]);
                if (exit != CleanArchCommand.Succeeded)
                {
                    return exit;
                }

                return Dotnet.Run(parseResult, root, ["test", "--solution", SolutionLocator.Relative(root, run ?? solution), "--configuration", "Debug", "--no-build"]);
            }
            finally
            {
                SolutionFilter.Delete(run);
                SolutionFilter.Delete(build);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            parseResult.InvocationConfiguration.Error.WriteLine(error.Message);
            return CleanArchCommand.Failed;
        }
    }
}

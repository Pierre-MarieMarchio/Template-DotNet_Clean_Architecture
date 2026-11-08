using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Test;

internal static class TestCommand
{
    public const string Name = "test";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private sealed record Selection(string? RunFilter, string? BuildFilter, bool LeavesOut, string[] NotRun, string[] Skipped);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Run this project's tests.");
        Option<bool> noIntegration = new("--no-integration")
        {
            Description = "Build every test project, and run only the ones that set IsTestProject and reference neither "
                + "Testcontainers nor Aspire.Hosting.Testing, so no Docker daemon is needed; it names each project it built and did not run.",
        };
        Option<string> summary = new("--summary")
        {
            Description = $"Write a TRX report of each test module under {TestSummary.ResultsFolder}, then print their totals and append them, as Markdown, to this file; a run that executed no test fails.",
        };
        Option<bool> reportsOnly = new("--reports-only")
        {
            Description = $"Total the TRX reports already under {TestSummary.ResultsFolder}, without running the tests; a run that executed no test fails.",
        };
        command.Options.Add(noIntegration);
        command.Options.Add(summary);
        command.Options.Add(reportsOnly);
        BuildSettings.Options settings = BuildSettings.Add(command);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult =>
            {
                Shell shell = context.Shell(parseResult);
                IReadOnlyList<string>? summaries = Summaries(parseResult.GetValue(summary), parseResult.GetValue(reportsOnly), context.WorkingDirectory);
                return parseResult.GetValue(reportsOnly)
                    ? TestRuns.Guarded(shell, () => TestSummary.Write(shell, project.Root, summaries!))
                    : Run(project, shell, parseResult.GetValue(noIntegration), settings.Of(parseResult), summaries);
            });
        }

        return command;
    }

    private static IReadOnlyList<string>? Summaries(string? summary, bool reportsOnly, string workingDirectory)
    {
        if (summary is null && !reportsOnly)
        {
            return null;
        }

        string? given = summary is null ? null : Path.GetFullPath(summary, workingDirectory);
        return [.. new[] { given, Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") }.OfType<string>().Where(path => path.Length > 0)];
    }

    private static int Run(LocatedProject project, Shell shell, bool dockerFreeOnly, BuildSettings settings, IReadOnlyList<string>? summaries) =>
        TestRuns.Guarded(shell, () =>
        {
            if (summaries is not null)
            {
                TestSummary.ForgetEarlierReports(project.Root);
            }

            Selection selection = Select(project, shell.Output, dockerFreeOnly);
            try
            {
                int exit = Execute(project, shell, settings, selection, summaries is null ? [] : TestSummary.ReportOptions);
                int summarised = summaries is null ? ExitCode.Succeeded : TestSummary.Write(shell, project.Root, summaries);
                return exit != ExitCode.Succeeded ? exit : summarised;
            }
            finally
            {
                SolutionFilter.Delete(selection.RunFilter);
                SolutionFilter.Delete(selection.BuildFilter);
                NameLeftOut(project.Root, shell.Output, selection);
            }
        });

    private static Selection Select(LocatedProject project, TextWriter output, bool dockerFreeOnly)
    {
        string root = project.Root;
        string solution = SolutionOf(project);
        string[] tests = TestProjects.All(root);
        string[] dockerFree = [.. tests.Where(TestProjects.IsDockerFree)];
        string[] notRun = dockerFreeOnly ? [.. tests.Except(dockerFree, StringComparer.Ordinal)] : [];
        string[] skipped = TestProjects.SkippedHere(tests);
        bool leavesOut = skipped.Length > 0 || notRun.Length > 0;
        Selection selection = dockerFreeOnly
            ? DockerFree(root, solution, tests, new Selection(null, null, leavesOut, notRun, skipped))
            : new Selection(Runnable(root, solution, skipped), null, leavesOut, notRun, skipped);
        NameLeftOut(root, output, selection);
        return selection;
    }

    private static Selection DockerFree(string root, string solution, string[] tests, Selection selection) =>
        selection with
        {
            RunFilter = SolutionFilter.WriteDockerFree(root, solution, tests, selection.Skipped),
            BuildFilter = selection.LeavesOut ? SolutionFilter.Write(root, solution, "docker-free-build.slnf", tests) : null,
        };

    private static string? Runnable(string root, string solution, string[] skipped) =>
        skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;

    private static void NameLeftOut(string root, TextWriter output, Selection selection)
    {
        foreach (string test in selection.NotRun)
        {
            output.WriteLine(TestProjects.NotRunLine(root, test));
        }

        TestRuns.Name(root, selection.Skipped, output);
    }

    private static int Execute(LocatedProject project, Shell shell, BuildSettings settings, Selection selection, IReadOnlyList<string> reporting)
    {
        string root = project.Root;
        string solution = SolutionOf(project);
        string target = ProjectPath.Relative(root, selection.RunFilter ?? solution);
        if (settings.MaxCpuCount is null && !selection.LeavesOut)
        {
            return shell.Run(Dotnet.Test(root, target, settings.Configuration, reporting));
        }

        return ExitCode.FirstFailureOf(
        [
            () => shell.Run(Dotnet.Build(root, ProjectPath.Relative(root, selection.BuildFilter ?? selection.RunFilter ?? solution), settings.Configuration, settings.Nodes)),
            () => shell.Run(Dotnet.Test(root, target, settings.Configuration, ["--no-build", .. settings.Modules, .. reporting])),
        ]);
    }

    private static string SolutionOf(LocatedProject project) => Path.Combine(project.Root, ProjectLayout.Solution(project));
}

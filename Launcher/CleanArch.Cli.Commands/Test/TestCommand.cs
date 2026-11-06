using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Test;

internal static class TestCommand
{
    public const string Name = "test";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private sealed record Selection(string? RunFilter, string? BuildFilter, bool LeavesOut);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Run this project's tests.");
        Option<bool> noIntegration = new("--no-integration")
        {
            Description = "Build every test project, and run only the ones that set IsTestProject and reference neither "
                + "Testcontainers nor Aspire.Hosting.Testing, so no Docker daemon is needed; it names each project it built and did not run.",
        };
        command.Options.Add(noIntegration);
        BuildSettings.Options settings = BuildSettings.Add(command);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult.GetValue(noIntegration), settings.Of(parseResult)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, bool dockerFreeOnly, BuildSettings settings) =>
        TestRuns.Guarded(shell, () =>
        {
            Selection selection = Select(project, shell.Output, dockerFreeOnly);
            try
            {
                return Execute(project, shell, settings, selection);
            }
            finally
            {
                SolutionFilter.Delete(selection.RunFilter);
                SolutionFilter.Delete(selection.BuildFilter);
            }
        });

    private static Selection Select(LocatedProject project, TextWriter output, bool dockerFreeOnly)
    {
        string root = project.Root;
        string solution = SolutionOf(project);
        string[] tests = TestProjects.All(root);
        string[] dockerFree = [.. tests.Where(TestProjects.IsDockerFree)];
        string[] notRun = dockerFreeOnly ? [.. tests.Except(dockerFree, StringComparer.Ordinal)] : [];
        foreach (string test in notRun)
        {
            output.WriteLine(TestProjects.NotRunLine(root, test));
        }

        string[] skipped = TestRuns.Skipped(root, output);
        bool leavesOut = skipped.Length > 0 || notRun.Length > 0;
        if (!dockerFreeOnly)
        {
            return new Selection(skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null, null, leavesOut);
        }

        string run = SolutionFilter.WriteDockerFree(root, solution, tests, skipped);
        string? build = leavesOut ? SolutionFilter.Write(root, solution, "docker-free-build.slnf", tests) : null;
        return new Selection(run, build, leavesOut);
    }

    private static int Execute(LocatedProject project, Shell shell, BuildSettings settings, Selection selection)
    {
        string root = project.Root;
        string solution = SolutionOf(project);
        string target = ProjectPath.Relative(root, selection.RunFilter ?? solution);
        if (settings.MaxCpuCount is null && !selection.LeavesOut)
        {
            return shell.Run(Dotnet.Test(root, target, settings.Configuration));
        }

        return ExitCode.FirstFailureOf(
        [
            () => shell.Run(Dotnet.Build(root, ProjectPath.Relative(root, selection.BuildFilter ?? selection.RunFilter ?? solution), settings.Configuration, settings.Nodes)),
            () => shell.Run(Dotnet.Test(root, target, settings.Configuration, ["--no-build", .. settings.Modules])),
        ]);
    }

    private static string SolutionOf(LocatedProject project) => Path.Combine(project.Root, ProjectLayout.Solution(project));
}

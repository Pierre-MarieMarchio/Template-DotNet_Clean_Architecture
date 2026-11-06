using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Check;

internal static class CheckCommand
{
    public const string Name = "check";

    public const string NothingTouched =
        "Nothing is touched: the working tree and this branch hold no change from the base, so there is nothing to check.";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(
            Name,
            "While you work, over what git diff touches since the merge-base with origin/develop, or the first of origin/HEAD, origin/main, "
            + "origin/master, develop, main and master that exists, with the files not yet tracked: the format of the C# files touched, "
            + "the build of the projects touched and of every project that references them, or of the solution when a file that sets the "
            + "build for all of them is touched, then the architecture tests and the unit tests among them. It builds the projects that "
            + "need Docker without running them, leaves out off Windows the ones whose TargetFramework names Windows, and names both; "
            + "the rest of the gate is left to verify, coverage and CI.");
        BuildSettings.Options settings = BuildSettings.Add(command);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), settings.Of(parseResult)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings) =>
        TestRuns.Guarded(shell, () => Check(project, shell, settings, OperatingSystem.IsWindows()));

    private static int Check(LocatedProject project, Shell shell, BuildSettings settings, bool onWindows)
    {
        string[] changed = TouchedFiles.Of(project.Root, shell);
        if (changed.Length == 0)
        {
            shell.Output.WriteLine(NothingTouched);
            return ExitCode.Succeeded;
        }

        CheckPlan plan = CheckPlan.Of(project, changed, onWindows);
        shell.Output.WriteLine(plan.ScopeLine);
        int exit = ExitCode.FirstFailureOf([() => FormatTouched(project, shell, plan), () => BuildAndTest(shell, settings, plan)]);
        if (exit == ExitCode.Succeeded)
        {
            shell.Output.WriteLine(plan.Summary);
        }

        return exit;
    }

    private static int FormatTouched(LocatedProject project, Shell shell, CheckPlan plan)
    {
        if (plan.Formatted.Length == 0)
        {
            return ExitCode.Succeeded;
        }

        int exit = shell.Run(Dotnet.StyleCheck(plan.Root, ProjectLayout.Solution(project), ["--include", .. plan.Formatted]));
        if (exit != ExitCode.Succeeded)
        {
            shell.Error.WriteLine($"Run `{Tool.Name} format`: it fixes what it can and names what it cannot.");
        }

        return exit;
    }

    private static int BuildAndTest(Shell shell, BuildSettings settings, CheckPlan plan)
    {
        List<string> filters = [];
        try
        {
            string build = plan.Scope.BuildWide ? plan.Solution : Kept(filters, Filter(plan, "check-build.slnf", plan.Built));
            string test = Kept(filters, plan.Scope.BuildWide ? DockerFreeFilter(plan) : Filter(plan, "check-test.slnf", plan.Tested));
            return ExitCode.FirstFailureOf(
            [
                () => shell.Run(Dotnet.Build(plan.Root, ProjectPath.Relative(plan.Root, build), settings.Configuration, settings.Nodes)),
                () => Announced(shell, plan, () => shell.Run(Dotnet.Test(plan.Root, ProjectPath.Relative(plan.Root, test), settings.Configuration, ["--no-build", .. settings.Modules]))),
            ]);
        }
        finally
        {
            foreach (string filter in filters)
            {
                SolutionFilter.Delete(filter);
            }
        }
    }

    private static string Kept(List<string> filters, string filter)
    {
        filters.Add(filter);
        return filter;
    }

    private static string Filter(CheckPlan plan, string fileName, IEnumerable<string> projects) =>
        SolutionFilter.Write(plan.Root, plan.Solution, fileName, projects.Select(project => FullPath(plan, project)));

    private static string DockerFreeFilter(CheckPlan plan)
    {
        string[] dockerFree = [.. TestProjects.All(plan.Root).Where(TestProjects.IsDockerFree)];
        return dockerFree.Length > 0
            ? SolutionFilter.Write(plan.Root, plan.Solution, "docker-free.slnf", dockerFree.Except(TestProjects.SkippedHere(dockerFree), StringComparer.Ordinal))
            : throw new InvalidOperationException(TestProjects.NoneFound);
    }

    private static int Announced(Shell shell, CheckPlan plan, Func<int> run)
    {
        Announce(shell, plan);
        try
        {
            return run();
        }
        finally
        {
            Announce(shell, plan);
        }
    }

    private static void Announce(Shell shell, CheckPlan plan)
    {
        foreach (string project in plan.NotRun)
        {
            shell.Output.WriteLine(TestProjects.NotRunLine(plan.Root, FullPath(plan, project)));
        }

        foreach (string project in plan.Skipped)
        {
            shell.Output.WriteLine(TestProjects.SkipLine(plan.Root, FullPath(plan, project)));
        }
    }

    private static string FullPath(CheckPlan plan, string project) => Path.GetFullPath(Path.Combine(plan.Root, project));
}

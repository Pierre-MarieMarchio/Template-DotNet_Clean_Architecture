namespace CleanArch.Cli.Commands.Kernel;

internal static class TestRuns
{
    public static string[] Skipped(string root, TextWriter output)
    {
        string[] skipped = TestProjects.SkippedHere(TestProjects.All(root));
        Name(root, skipped, output);
        return skipped;
    }

    public static void Name(string root, IEnumerable<string> skipped, TextWriter output)
    {
        foreach (string test in skipped)
        {
            output.WriteLine(TestProjects.SkipLine(root, test));
        }
    }

    public static int AroundSkipped(string root, TextWriter output, Func<string[], int> run)
    {
        string[] skipped = Skipped(root, output);
        try
        {
            return run(skipped);
        }
        finally
        {
            Name(root, skipped, output);
        }
    }

    public static int RunBuilt(LocatedProject project, Shell shell, BuildSettings settings) =>
        Guarded(shell, () =>
        {
            string root = project.Root;
            string solution = Path.Combine(root, ProjectLayout.Solution(project));
            return AroundSkipped(root, shell.Output, skipped =>
            {
                string? run = skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
                try
                {
                    return shell.Run(Dotnet.Test(root, ProjectPath.Relative(root, run ?? solution), settings.Configuration, ["--no-build", .. settings.Modules]));
                }
                finally
                {
                    SolutionFilter.Delete(run);
                }
            });
        });

    public static int Guarded(Shell shell, Func<int> run)
    {
        try
        {
            return run();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            return shell.Fail(error.Message);
        }
    }
}

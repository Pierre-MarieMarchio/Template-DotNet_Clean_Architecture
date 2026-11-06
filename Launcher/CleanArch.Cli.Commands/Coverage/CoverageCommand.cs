using System.CommandLine;
using System.Globalization;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Coverage;

internal static class CoverageCommand
{
    public const string Name = "coverage";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var command = new Command(Name, "Run this project's tests with coverage, then check the floor coverage.minimum states.");
        BuildSettings.Options settings = BuildSettings.Add(command);
        command.SetAction(parseResult => Run(context, parseResult, settings.Of(parseResult)));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, BuildSettings settings)
    {
        if (context.Project is not { } project)
        {
            return CleanArchCommand.Failed;
        }

        Shell shell = context.Shell(parseResult);
        string root = project.Root;
        string solution = Path.Combine(project.Root, ProjectLayout.Solution(project));
        string results = Path.Combine(root, "TestResults");
        TextWriter output = shell.Output;
        try
        {
            if (Directory.Exists(results))
            {
                Directory.Delete(results, recursive: true);
            }

            string[] skipped = TestProjects.SkippedHere(TestProjects.All(root));
            foreach (string test in skipped)
            {
                output.WriteLine(TestProjects.SkipLine(root, test));
            }

            string[] built = [];
            if (settings.MaxCpuCount is not null || skipped.Length > 0)
            {
                int exit = Dotnet.Run(shell, root, ["build", SolutionLocator.Relative(root, solution), "--configuration", settings.Configuration, .. settings.Nodes]);
                if (exit != CleanArchCommand.Succeeded)
                {
                    return exit;
                }

                built = ["--no-build"];
            }

            string? filter = skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
            try
            {
                int exit = Dotnet.Run(
                    shell,
                    root,
                    [
                        "test", "--solution", SolutionLocator.Relative(root, filter ?? solution),
                        "--configuration", settings.Configuration,
                        .. built,
                        .. settings.Modules,
                        "--results-directory", SolutionLocator.Relative(root, results),
                        "--coverage",
                        "--coverage-settings", SolutionLocator.Relative(root, Path.Combine(root, "coverage.runsettings")),
                        "--coverage-output-format", "cobertura",
                    ]);
                if (exit != CleanArchCommand.Succeeded)
                {
                    return exit;
                }

                string minimum = CoverageMinimum(Path.Combine(root, "coverage.minimum"));
                return Dotnet.Run(
                    shell,
                    root,
                    [
                        "run", SolutionLocator.Relative(root, Path.Combine(root, "Tools", "CoverageGate.cs")),
                        "--root", SolutionLocator.Relative(root, results),
                        "--minimum", minimum,
                    ]);
            }
            finally
            {
                SolutionFilter.Delete(filter);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or IOException)
        {
            shell.Error.WriteLine(error.Message);
            return CleanArchCommand.Failed;
        }
    }

    private static string CoverageMinimum(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string candidate = line.Trim();
            if (double.TryParse(candidate, CultureInfo.InvariantCulture, out _))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"'{path}' states no coverage floor: no line is a bare number.");
    }
}

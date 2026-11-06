using System.CommandLine;
using System.Globalization;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Coverage;

internal static class CoverageCommand
{
    public const string Name = "coverage";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Run this project's tests with coverage, then check the floor coverage.minimum states.");
        BuildSettings.Options settings = BuildSettings.Add(command);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), settings.Of(parseResult)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings) =>
        TestRuns.Guarded(shell, () =>
        {
            string root = project.Root;
            string solution = Path.Combine(root, ProjectLayout.Solution(project));
            string results = Path.Combine(root, "TestResults");
            if (Directory.Exists(results))
            {
                Directory.Delete(results, recursive: true);
            }

            string[] skipped = TestRuns.Skipped(root, shell.Output);
            bool built = settings.MaxCpuCount is not null || skipped.Length > 0;
            int exit = built
                ? shell.Run(Dotnet.Build(root, ProjectPath.Relative(root, solution), settings.Configuration, settings.Nodes))
                : ExitCode.Succeeded;
            if (exit != ExitCode.Succeeded)
            {
                return exit;
            }

            string? filter = skipped.Length > 0 ? SolutionFilter.WriteRunnable(root, solution, skipped) : null;
            try
            {
                return ExitCode.FirstFailureOf(
                [
                    () => shell.Run(CoveredTests(root, ProjectPath.Relative(root, filter ?? solution), settings, built, results)),
                    () => shell.Run(CoverageGate(root, results)),
                ]);
            }
            finally
            {
                SolutionFilter.Delete(filter);
            }
        });

    private static ExternalCall CoveredTests(string root, string target, BuildSettings settings, bool built, string results) =>
        Dotnet.Test(
            root,
            target,
            settings.Configuration,
            [
                .. built ? ["--no-build"] : Array.Empty<string>(),
                .. settings.Modules,
                "--results-directory", ProjectPath.Relative(root, results),
                "--coverage",
                "--coverage-settings", ProjectPath.Relative(root, Path.Combine(root, "coverage.runsettings")),
                "--coverage-output-format", "cobertura",
            ]);

    private static ExternalCall CoverageGate(string root, string results) =>
        Dotnet.RunFile(
            root,
            ProjectPath.Shown(ProjectLayout.Tool("CoverageGate.cs")),
            "--root", ProjectPath.Relative(root, results),
            "--minimum", CoverageMinimum(Path.Combine(root, "coverage.minimum")));

    private static string CoverageMinimum(string path) =>
        File.ReadLines(path).Select(line => line.Trim()).FirstOrDefault(candidate => double.TryParse(candidate, CultureInfo.InvariantCulture, out _))
        ?? throw new InvalidOperationException($"'{path}' states no coverage floor: no line is a bare number.");
}

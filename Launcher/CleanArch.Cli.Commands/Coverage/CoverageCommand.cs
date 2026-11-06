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
        Option<bool> reportsOnly = new("--reports-only")
        {
            Description = "Judge the Cobertura reports already under TestResults against the floor, without running the tests.",
        };
        Option<string> summary = new("--summary") { Description = "Append the coverage table, as Markdown, to this file." };
        Option<string> json = new("--json") { Description = "Write the merged totals, as JSON, to this file." };
        command.Options.Add(reportsOnly);
        command.Options.Add(summary);
        command.Options.Add(json);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult =>
            {
                Shell shell = context.Shell(parseResult);
                var outputs = CoverageOutputs.From(
                    InFolder(parseResult.GetValue(summary), context.WorkingDirectory),
                    Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY"),
                    InFolder(parseResult.GetValue(json), context.WorkingDirectory));
                return parseResult.GetValue(reportsOnly)
                    ? TestRuns.Guarded(shell, () => Judge(project, shell, outputs))
                    : Run(project, shell, settings.Of(parseResult), outputs);
            });
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings, CoverageOutputs outputs) =>
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
                    () => Judge(project, shell, outputs),
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

    private static string? InFolder(string? path, string folder) => string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path, folder);

    private static int Judge(LocatedProject project, Shell shell, CoverageOutputs outputs) =>
        CoverageFloor.Judge(
            shell,
            project.Root,
            double.Parse(CoverageMinimum(Path.Combine(project.Root, "coverage.minimum")), CultureInfo.InvariantCulture),
            outputs);

    private static string CoverageMinimum(string path) =>
        File.ReadLines(path).Select(line => line.Trim()).FirstOrDefault(candidate => double.TryParse(candidate, CultureInfo.InvariantCulture, out _))
        ?? throw new InvalidOperationException($"'{path}' states no coverage floor: no line is a bare number.");
}

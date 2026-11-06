using System.CommandLine;
using CleanArch.Tooling.Failures;

namespace CleanArch.Cli.UpgradeReport;

internal static class UpgradeReportCommand
{
    public const string Name = "upgrade-report";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(
            Name,
            "Say what a newer version of the package that generated this project changes, from both versions' generations compared with its files; write nothing in the project.");
        Option<string> to = new("--to")
        {
            Description = "The target version; the latest stable version the sources hold when left out, so a prerelease is the target only when named here.",
        };
        Option<string[]> source = new("--source")
        {
            Description = "A .nupkg of the package, or a folder holding some, read in place of the NuGet sources; repeat it to read several.",
        };
        Option<string> json = new("--json") { Description = "Also write the report as JSON to this file, outside the project." };
        Option<string> keep = new("--keep")
        {
            Description = "Keep the two generations, as <folder>/<version>, outside the project: the files to take come from there.",
        };
        command.Options.Add(to);
        command.Options.Add(source);
        command.Options.Add(json);
        command.Options.Add(keep);

        if (context.Project is { } project)
        {
            command.SetAction(parseResult =>
            {
                string? FullPath(string? path) => path is null ? null : Path.GetFullPath(path, context.WorkingDirectory);
                var request = new UpgradeRequest(
                    parseResult.GetValue(to),
                    [.. (parseResult.GetValue(source) ?? []).Select(path => FullPath(path)!)],
                    FullPath(parseResult.GetValue(json)),
                    FullPath(parseResult.GetValue(keep)));

                return Run(context, project, context.Shell(parseResult), request);
            });
        }

        return command;
    }

    private static int Run(CommandContext context, LocatedProject project, Shell shell, UpgradeRequest request)
    {
        try
        {
            Report report = new ReportBuilder(new ReportProcesses(context.Processes, shell)).Build(project.Root, Provenance.Of(project), request);
            ReportWriter.Print(report, shell.Output);
            return ExitCode.Succeeded;
        }
        catch (LauncherException refusal)
        {
            shell.Output.Flush();
            return shell.Fail(refusal.Message);
        }
    }
}

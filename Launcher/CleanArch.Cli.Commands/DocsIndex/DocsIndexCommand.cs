using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.DocsIndex;

internal static class DocsIndexCommand
{
    public const string Name = "docs-index";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Write an index.md into each folder under docs/ that holds a page, from the frontmatter of each page.");
        Option<bool> check = new("--check")
        {
            Description = "Fail when an index differs from what the command writes, or is kept for a folder that holds no page, instead of writing.",
        };
        command.Options.Add(check);
        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(project, context.Shell(parseResult), parseResult.GetValue(check)));
        }

        return command;
    }

    private static int Run(LocatedProject project, Shell shell, bool checking) =>
        TestRuns.Guarded(shell, () =>
        {
            if (checking)
            {
                DocumentationIndex.Check(project.Root, shell.Output);
            }
            else
            {
                DocumentationIndex.Write(project.Root, shell.Output);
            }

            return ExitCode.Succeeded;
        });
}

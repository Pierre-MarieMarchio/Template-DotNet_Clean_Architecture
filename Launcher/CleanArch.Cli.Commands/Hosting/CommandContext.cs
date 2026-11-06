using System.CommandLine;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Hosting;

internal sealed record CommandContext(string WorkingDirectory, LocatedProject? Project, ProjectFileException? RefusedProjectFile, IProcessLauncher Processes)
{
    public IReadOnlyList<LocalCommand> LocalCommands { get; init; } = [];

    public static CommandContext Locate(string workingDirectory, IProcessLauncher processes)
    {
        try
        {
            LocatedProject? project = ProjectLocator.Find(workingDirectory);
            return new CommandContext(workingDirectory, project, null, processes) { LocalCommands = LocalCommand.In(project) };
        }
        catch (ProjectFileException refused)
        {
            return new CommandContext(workingDirectory, null, refused, processes);
        }
    }

    public Shell Shell(ParseResult parseResult) =>
        Shell(parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error);

    public Shell Shell(TextWriter output, TextWriter error) => new(Processes, output, error);

    public string OutsideProject =>
        $"Not inside a CleanArch project: no {ProjectLocator.FileName} in {WorkingDirectory} or any folder above it.";
}

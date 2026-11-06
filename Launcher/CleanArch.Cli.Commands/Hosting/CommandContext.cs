using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Hosting;

internal sealed record CommandContext(string WorkingDirectory, LocatedProject? Project, ProjectFileException? RefusedProjectFile)
{
    public IReadOnlyList<LocalCommand> LocalCommands { get; init; } = [];

    public static CommandContext Locate(string workingDirectory)
    {
        try
        {
            LocatedProject? project = ProjectLocator.Find(workingDirectory);
            return new CommandContext(workingDirectory, project, null) { LocalCommands = LocalCommand.In(project) };
        }
        catch (ProjectFileException refused)
        {
            return new CommandContext(workingDirectory, null, refused);
        }
    }

    public string OutsideProject =>
        $"Not inside a CleanArch project: no {ProjectLocator.FileName} in {WorkingDirectory} or any folder above it.";
}

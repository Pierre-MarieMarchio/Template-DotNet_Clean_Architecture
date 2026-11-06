namespace CleanArch.Cli;

internal sealed record CommandContext(string WorkingDirectory, LocatedProject? Project, ProjectFileException? RefusedProjectFile)
{
    public static CommandContext Locate(string workingDirectory)
    {
        try
        {
            return new CommandContext(workingDirectory, ProjectLocator.Find(workingDirectory), null);
        }
        catch (ProjectFileException refused)
        {
            return new CommandContext(workingDirectory, null, refused);
        }
    }

    public string OutsideProject =>
        $"Not inside a CleanArch project: no {ProjectLocator.FileName} in {WorkingDirectory} or any folder above it.";
}

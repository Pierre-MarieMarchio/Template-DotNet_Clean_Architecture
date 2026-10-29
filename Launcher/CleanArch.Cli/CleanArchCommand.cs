using System.CommandLine;

namespace CleanArch.Cli;

public static class CleanArchCommand
{
    public const int Succeeded = 0;
    public const int Failed = 1;

    public static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error)
    {
        var root = new RootCommand("The tool of a project generated from the CleanArch.WebApi.Templates package.");
        root.SetAction(_ => Describe(workingDirectory, output, error));

        return root.Parse([.. arguments]).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }

    private static int Describe(string workingDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            LocatedProject? project = ProjectLocator.Find(workingDirectory);
            if (project is null)
            {
                error.WriteLine($"Not inside a CleanArch project: no {ProjectLocator.FileName} in {workingDirectory} or any folder above it.");
                return Failed;
            }

            output.WriteLine($"{project.File.Name}, generated from {project.File.Template.Package} {project.File.Template.Version}, at {project.Root}");
            return Succeeded;
        }
        catch (ProjectFileException refused)
        {
            error.WriteLine(refused.Message);
            return Failed;
        }
    }
}

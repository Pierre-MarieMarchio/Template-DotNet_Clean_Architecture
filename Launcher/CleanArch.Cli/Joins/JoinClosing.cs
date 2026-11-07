namespace CleanArch.Cli.Joins;

internal static class JoinClosing
{
    public static int Refused(TextWriter error, Action join)
    {
        try
        {
            join();
            return ExitCode.Succeeded;
        }
        catch (JoinRefusedException refusal)
        {
            error.WriteLine(refusal.Message);
            return ExitCode.Failed;
        }
    }

    public static void Indexed(LocatedProject project, TextWriter output)
    {
        DocumentationIndex.Write(project.Root, output);
        if (ProjectLayout.HoldsTheApi(project))
        {
            output.WriteLine($"Next, once the routes and contracts are as they should be: {Tool.Name} openapi");
        }
    }

    public static void RecordClient(LocatedProject project, ProjectRecord record)
    {
        if (Directory.Exists(Path.Combine(project.Root, ProjectLayout.Client(project))))
        {
            record.RecordClient();
        }
    }
}

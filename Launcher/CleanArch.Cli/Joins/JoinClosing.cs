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
            ApiSteps(project, output);
        }
    }

    public static void RecordClient(LocatedProject project, ProjectRecord record)
    {
        if (Directory.Exists(Path.Combine(project.Root, ProjectLayout.Client(project))))
        {
            record.RecordClient();
        }
    }

    private static void ApiSteps(LocatedProject project, TextWriter output)
    {
        output.WriteLine("Next, once the routes and contracts are as they should be:");
        output.WriteLine("  dotnet run Tools/Tasks.cs openapi");

        if (Directory.Exists(Path.Combine(project.Root, ProjectLayout.Client(project))))
        {
            output.WriteLine("  dotnet run Tools/Tasks.cs api-client");
        }

        if (WebSchema.IsGenerated(project))
        {
            output.WriteLine("  pnpm api    (in Src/Web)");
        }
    }
}

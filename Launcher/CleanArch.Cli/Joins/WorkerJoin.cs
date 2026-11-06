using CleanArch.Tooling;

namespace CleanArch.Cli.Joins;

internal static class WorkerJoin
{
    public static void Run(JoinProcesses processes, LocatedProject project, string relative)
    {
        string root = project.Root;
        string readme = $"{relative}/README.md";
        Require(project, readme);

        List<ReadmeEdit> edits = ReadmeEdits.Listed(root, readme);
        Dictionary<string, List<string>> files = EditPlacement.Read(root, edits);
        PlacedEdits placed = EditPlacement.Make(edits, files);
        IReadOnlyList<FileMove> moves = JobMoves.Of(project);
        IReadOnlyList<NamespaceRename> namespaces = JobMoves.Namespaces(project, moves);
        string[] leaving = [.. moves.Select(move => move.From)];

        var writes = new PendingWrites(root);
        writes.Edited(placed.Made, files);
        writes.Moved(moves, text => JobMoves.Renamed(text, namespaces));
        writes.Renamed(
            namespaces.Count == 0 ? [] : ProjectTree.TextFiles(root).Where(path => !leaving.Contains(path, StringComparer.Ordinal)),
            text => JobMoves.Renamed(text, namespaces));
        writes.WithoutTheJoiningSection(readme);

        foreach (ReadmeEdit edit in placed.InPlace)
        {
            processes.Output.WriteLine($"In place already: {edit}");
        }

        string[] touched = [.. leaving, .. writes.Paths.Except(moves.Select(move => move.To), StringComparer.Ordinal)];
        if (touched.Length > 0)
        {
            CommittedTree.Require(processes, root, touched, [relative + "/", $"Tests/Presentation/{ProjectNames.FolderOf(project.File.Name, JobMoves.Worker + ".UnitTests")}/"]);
        }

        TrialBuild.Run(processes, project, writes, moves);
        writes.WriteTo(root, leaving);
        Report(processes.Output, project, relative, moves, namespaces, placed.Made);
    }

    private static void Require(LocatedProject project, string readme)
    {
        if (!Directory.Exists(Path.Combine(project.Root, ProjectLayout.Api(project))))
        {
            throw new JoinRefusedException(
                $"{project.File.Name} holds no API, whose jobs the worker takes: a project without the API runs them in its worker already, so no file was changed.");
        }

        if (!File.Exists(Path.Combine(project.Root, readme)))
        {
            throw new JoinRefusedException($"{readme} does not exist: name the folder cleanarch-presentation wrote under Src/Presentation.");
        }
    }

    private static void Report(
        TextWriter output, LocatedProject project, string relative, IReadOnlyList<FileMove> moves, IReadOnlyList<NamespaceRename> namespaces, IReadOnlyList<ReadmeEdit> made)
    {
        foreach (FileMove move in moves)
        {
            output.WriteLine($"Moved: {move.From} to {move.To}");
        }

        foreach (NamespaceRename space in namespaces)
        {
            output.WriteLine($"Renamed: {space.From} to {space.To}");
        }

        foreach (ReadmeEdit edit in made)
        {
            output.WriteLine($"Applied: {edit}");
        }

        output.WriteLine(
            $"Joined {relative} to the project, the jobs with it. Run `dotnet run Tools/Tasks.cs presentation-scaffold {project.File.Name}.{JobMoves.Worker}` "
            + "next: it adds the worker and its tests to the solution.");
    }
}

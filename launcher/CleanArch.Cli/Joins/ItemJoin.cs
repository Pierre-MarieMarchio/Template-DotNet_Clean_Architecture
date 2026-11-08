namespace CleanArch.Cli.Joins;

internal sealed record JoinedItem(string Folder, string ItemTemplate, bool Required, string? WorkspaceReadme = null)
{
    public string Readme => $"{Folder}/README.md";
}

internal static class ItemJoin
{
    public static void Run(string root, JoinedItem item, TextWriter output)
    {
        List<ReadmeEdit> edits = Edits(root, item);
        if (edits.Count == 0)
        {
            output.WriteLine($"{item.Folder} lists no edit outside its folder.");
            return;
        }

        Dictionary<string, List<string>> files = EditPlacement.Read(root, edits);
        PlacedEdits placed = EditPlacement.Make(edits, files);

        foreach (ReadmeEdit edit in placed.InPlace)
        {
            output.WriteLine($"In place already: {edit}");
        }

        if (placed.Made.Count == 0)
        {
            string listed = string.Join(" and ", edits.Select(edit => edit.Readme).Distinct(StringComparer.Ordinal));
            output.WriteLine($"Every edit {listed} lists is in place already, so no file was changed.");
            return;
        }

        foreach (string path in placed.Made.Select(edit => edit.Path).Distinct(StringComparer.Ordinal))
        {
            ProjectTree.WriteLines(root, path, files[path]);
        }

        foreach (ReadmeEdit edit in placed.Made)
        {
            output.WriteLine($"Applied: {edit}");
        }

        output.WriteLine($"Joined {item.Folder} to the project.");
    }

    private static List<ReadmeEdit> Edits(string root, JoinedItem item)
    {
        string readme = Path.Combine(root, item.Readme);
        if (!Directory.Exists(Path.Combine(root, item.Folder)) || (item.Required && !File.Exists(readme)))
        {
            throw new JoinRefusedException(
                $"{item.Readme} does not exist: name the folder {item.ItemTemplate} wrote under {ProjectPath.Shown(Path.GetDirectoryName(item.Folder)!)}.");
        }

        var edits = new List<ReadmeEdit>();
        if (item.WorkspaceReadme is not null)
        {
            edits.AddRange(ReadmeEdits.Listed(root, item.WorkspaceReadme));
        }

        if (item.Required || ReadmeEdits.Lists(readme))
        {
            edits.AddRange(ReadmeEdits.Listed(root, item.Readme));
        }

        return edits;
    }
}

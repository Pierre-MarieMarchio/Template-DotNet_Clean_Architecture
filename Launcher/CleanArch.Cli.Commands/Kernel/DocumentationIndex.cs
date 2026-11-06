using System.Text;

namespace CleanArch.Cli.Commands.Kernel;

internal static class DocumentationIndex
{
    private const string _docsFolder = "docs";

    private const string _indexName = "index.md";

    private const string _readme = "README.md";

    private const string _notice =
        "Written by `dotnet run Tools/Tasks.cs docs-index` from the frontmatter of each page:\nedit the pages, then run it again.";

    private static readonly string[] _pagesOutsideTheIndex = [_indexName, "log.md"];

    private static readonly string[] _foldersOutsideTheIndex = ["plans"];

    private sealed record Entry(string Link, string Title, string Description, string? Status);

    public static int Written(LocatedProject project, Shell shell) =>
        TestRuns.Guarded(shell, () =>
        {
            Write(project.Root, shell.Output);
            return ExitCode.Succeeded;
        });

    public static int Checked(LocatedProject project, Shell shell) =>
        TestRuns.Guarded(shell, () =>
        {
            Check(project.Root, shell.Output);
            return ExitCode.Succeeded;
        });

    public static void Write(string root, TextWriter output)
    {
        string docs = Path.Combine(root, _docsFolder);
        Dictionary<string, string> indexes = Indexes(docs);

        foreach ((string path, string text) in indexes)
        {
            File.WriteAllText(Path.Combine(root, path), text, new UTF8Encoding(false));
        }

        foreach (string path in Stale(root, docs, indexes))
        {
            File.Delete(Path.Combine(root, path));
        }

        output.WriteLine($"Wrote {indexes.Count} index files under {_docsFolder}/.");
    }

    public static void Check(string root, TextWriter output)
    {
        string docs = Path.Combine(root, _docsFolder);
        Dictionary<string, string> indexes = Indexes(docs);
        string[] problems =
        [
            .. indexes.Where(index => Differs(root, index)).Select(index => Problem(root, index.Key)),
            .. Stale(root, docs, indexes).Select(path => $"{path} indexes a folder that holds no page"),
        ];

        if (problems.Length > 0)
        {
            throw new InvalidOperationException(
                $"The documentation index is out of date: {string.Join("; ", problems)}. "
                + "Run `dotnet run Tools/Tasks.cs docs-index` and commit what it writes.");
        }

        output.WriteLine($"The {indexes.Count} index files under {_docsFolder}/ list their folders' pages as their frontmatter says.");
    }

    private static bool Differs(string root, KeyValuePair<string, string> index)
    {
        string path = Path.Combine(root, index.Key);
        return !File.Exists(path) || File.ReadAllText(path) != index.Value;
    }

    private static string Problem(string root, string path) =>
        File.Exists(Path.Combine(root, path)) ? $"{path} differs from what it writes" : $"{path} is missing";

    private static string[] Stale(string root, string docs, Dictionary<string, string> indexes) =>
        Directory.Exists(docs)
            ? [.. IndexFiles(docs).Select(file => Relative(root, file)).Where(path => !indexes.ContainsKey(path))]
            : [];

    private static Dictionary<string, string> Indexes(string docs)
    {
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Directory.Exists(docs))
        {
            IndexFolder(Path.GetDirectoryName(docs)!, docs, indexes);
        }

        return indexes;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static IEnumerable<string> IndexFiles(string folder) =>
        Directory.EnumerateFiles(folder, _indexName)
            .Concat(IndexedFolders(folder).SelectMany(IndexFiles));

    private static IEnumerable<string> IndexedFolders(string folder) =>
        Directory.EnumerateDirectories(folder)
            .Where(child => Path.GetFileName(child) is var name
                && !name.StartsWith('_')
                && !name.StartsWith('.')
                && !_foldersOutsideTheIndex.Contains(name, StringComparer.Ordinal));

    private static Entry? IndexFolder(string root, string folder, Dictionary<string, string> indexes)
    {
        Entry[] pages = Pages(root, folder);
        Entry[] folders =
        [
            .. IndexedFolders(folder)
                .Select(child => IndexFolder(root, child, indexes))
                .OfType<Entry>()
                .OrderBy(entry => entry.Title, StringComparer.Ordinal),
        ];

        if (pages.Length == 0 && folders.Length == 0)
        {
            return null;
        }

        string name = Path.GetFileName(folder);
        Entry? readme = pages.FirstOrDefault(page => page.Link == _readme);
        string title = readme?.Title ?? char.ToUpperInvariant(name[0]) + name[1..].Replace('-', ' ');
        indexes[Relative(root, Path.Combine(folder, _indexName))] = Text(title, pages, folders);

        return new Entry($"{name}/{_indexName}", title, readme?.Description ?? string.Empty, null);
    }

    private static Entry[] Pages(string root, string folder) =>
    [
        .. Directory.EnumerateFiles(folder, "*.md")
            .Where(file => !_pagesOutsideTheIndex.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            .Select(file => PageEntry(root, file))
            .OrderBy(page => page.Link != _readme)
            .ThenBy(page => page.Title, StringComparer.Ordinal),
    ];

    private static string Text(string title, Entry[] pages, Entry[] folders)
    {
        var text = new StringBuilder().Append("# ").Append(title).Append("\n\n").Append(_notice).Append('\n');

        if (pages.Length > 0)
        {
            text.Append('\n').Append(string.Concat(pages.Select(Line)));
        }

        if (folders.Length > 0)
        {
            text.Append("\n## Folders\n\n").Append(string.Concat(folders.Select(Line)));
        }

        return text.ToString();
    }

    private static string Line(Entry entry) =>
        $"- [{entry.Title}]({entry.Link})" + Described(entry) + "\n";

    private static string Described(Entry entry)
    {
        if (entry.Description.Length == 0)
        {
            return string.Empty;
        }

        return " - " + (entry.Status is null ? string.Empty : entry.Status + ". ") + entry.Description;
    }

    private static Entry PageEntry(string root, string file)
    {
        string page = Relative(root, file);
        Dictionary<string, string> fields = Frontmatter(File.ReadAllText(file));

        string Field(string key) => fields.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : throw new InvalidOperationException(
                $"{page} has no {key} in a frontmatter at its first line, so the index cannot list it: "
                + "docs/README.md#adding-a-page says what each page's frontmatter holds.");

        return new Entry(
            Path.GetFileName(file),
            Field("title"),
            Field("description"),
            fields.GetValueOrDefault("type") == "decision" ? Field("status") : null);
    }

    private static Dictionary<string, string> Frontmatter(string content)
    {
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');
        int closing = lines[0] == "---" ? Array.IndexOf(lines, "---", 1) : -1;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in closing < 0 ? [] : lines[1..closing])
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);

            if (colon > 0)
            {
                string value = line[(colon + 2)..].Trim();
                fields[line[..colon]] = value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
            }
        }

        return fields;
    }
}

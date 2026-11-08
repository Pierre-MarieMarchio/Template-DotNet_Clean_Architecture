namespace CleanArch.Cli.UpgradeReport;

internal sealed class TreeComparison
{
    private const string _solutionEditors = "the scaffolds of add presentation and add web-app add the .NET projects they create to it";

    private readonly FoldedTree _older;
    private readonly FoldedTree _newer;
    private readonly FoldedTree _project;
    private readonly string _name;
    private readonly IReadOnlyDictionary<string, string> _joined;
    private readonly FileSameness _sameness;
    private readonly ScaffoldedParts _scaffolds;

    private readonly List<Finding> _findings = [];
    private readonly SortedDictionary<string, List<string>> _bootstrap = new(StringComparer.Ordinal);
    private readonly List<string> _scaffoldNotes = [];
    private readonly List<string> _own = [];
    private int _unchanged;
    private string? _provenance;

    private TreeComparison(Trees trees, IReadOnlyCollection<string> projectFiles, string name, IReadOnlyDictionary<string, string> joined, IReadOnlySet<string>? heldGuids)
    {
        _older = new FoldedTree(trees.Older, ProjectTree.Files(trees.Older));
        _newer = new FoldedTree(trees.Newer, ProjectTree.Files(trees.Newer));
        _project = new FoldedTree(trees.Project, projectFiles);
        _name = name;
        _joined = joined;
        _sameness = FileSameness.For(trees.Project, heldGuids);
        _scaffolds = ScaffoldedParts.In(_older, _newer);
    }

    public static Comparison Compare(Trees trees, IReadOnlyCollection<string> projectFiles, string name, IReadOnlyDictionary<string, string> joined, IReadOnlySet<string>? heldGuids)
    {
        var comparison = new TreeComparison(trees, projectFiles, name, joined, heldGuids);

        foreach (string path in comparison._older.Keys.Union(comparison._newer.Keys).Union(comparison._project.Keys).Order(StringComparer.Ordinal))
        {
            comparison.Classify(path);
        }

        return new Comparison(comparison._findings, comparison._bootstrap, comparison._scaffoldNotes, comparison._own, comparison._unchanged, comparison._provenance);
    }

    private void Classify(string path)
    {
        var at = new Presence(_older.Holds(path), _newer.Holds(path), _project.Holds(path));

        if (BootstrapWriters.Writer(path) is { } writer)
        {
            ListIfHeld(writer, path, at);
        }
        else if (path == ProjectLocator.FileName)
        {
            _provenance = "cleanarch.json is the project's record: set template.version to the target version, and keep the rest.";
        }
        else if (_scaffolds.FolderOf(path) is not { } folder || !Scaffolded(path, folder, at))
        {
            Templated(path, at);
        }
    }

    private void ListIfHeld(string writer, string path, Presence at)
    {
        if (!at.Project)
        {
            return;
        }

        if (!_bootstrap.TryGetValue(writer, out List<string>? paths))
        {
            _bootstrap[writer] = paths = [];
        }

        paths.Add(_project.PathOf(path));
    }

    private bool Scaffolded(string path, string folder, Presence at)
    {
        string inside = path[(folder.Length + 1)..];

        if (inside == ScaffoldedParts.Descriptor)
        {
            if (!UnchangedByTheTarget(path, at))
            {
                _scaffoldNotes.Add($"{Shown(path)}: {ChangeOf(at)} by the target version; the part was created from it, so take what the new descriptor brings by hand.");
            }

            return true;
        }

        if (inside.StartsWith("overlay/", StringComparison.Ordinal))
        {
            if (!UnchangedByTheTarget(path, at))
            {
                string target = folder + "/" + inside["overlay/".Length..];
                _findings.Add(new Finding(FindingKind.Merge, Shown(target), $"the part's scaffold laid {Shown(path)} over the part it created", at.Older ? _older.PathOf(path) : null, at.Newer ? _newer.PathOf(path) : null));
            }

            return true;
        }

        if (at.Project && !at.Older && !at.Newer)
        {
            ListIfHeld(_scaffolds.CommandOf(folder), path, at);
            return true;
        }

        return false;
    }

    private void Templated(string path, Presence at)
    {
        if (at.Older && at.Newer)
        {
            Changed(path, at.Project);
        }
        else if (at.Newer)
        {
            Added(path, at.Project);
        }
        else if (at.Older)
        {
            Removed(path, at.Project);
        }
        else
        {
            _own.Add(_project.PathOf(path));
        }
    }

    private void Changed(string path, bool inProject)
    {
        string? edited = BootstrapWriters.Editor(path)
            ?? (ProjectTree.IsTheSolution(path, _name) && _scaffolds.AddToTheSolution ? _solutionEditors : null);
        string? join = _joined.TryGetValue(path, out string? part) ? $"the join of {part} edits it" : null;

        if (Same(_older, _newer, path))
        {
            _unchanged++;
        }
        else if (!inProject)
        {
            _findings.Add(new Finding(FindingKind.Merge, Shown(path), Note("the project deleted it", join), _older.PathOf(path), _newer.PathOf(path)));
        }
        else if (edited is null && join is null && Same(_older, _project, path))
        {
            _findings.Add(new Finding(FindingKind.Take, Shown(path), null, _older.PathOf(path), _newer.PathOf(path)));
        }
        else
        {
            _findings.Add(new Finding(FindingKind.Merge, Shown(path), Note(edited, join), _older.PathOf(path), _newer.PathOf(path)));
        }
    }

    private void Added(string path, bool inProject)
    {
        if (!inProject)
        {
            _findings.Add(new Finding(FindingKind.Added, Shown(path), null, null, _newer.PathOf(path)));
        }
        else if (Same(_newer, _project, path))
        {
            _unchanged++;
        }
        else
        {
            _findings.Add(new Finding(FindingKind.Added, Shown(path), "the project holds a file of its own at this path: merge by hand", null, _newer.PathOf(path)));
        }
    }

    private void Removed(string path, bool inProject)
    {
        if (!inProject)
        {
            _unchanged++;
            return;
        }

        string? note = Same(_older, _project, path) ? null : "the project changed it: decide by hand";
        _findings.Add(new Finding(FindingKind.Removed, Shown(path), note, _older.PathOf(path), null));
    }

    private bool UnchangedByTheTarget(string path, Presence at) => at.Older && at.Newer && Same(_older, _newer, path);

    private bool Same(FoldedTree left, FoldedTree right, string path) => _sameness.Same(left.Full(path), right.Full(path));

    private string Shown(string path) => new[] { _project, _newer, _older }.FirstOrDefault(tree => tree.Holds(path))?.PathOf(path) ?? path;

    private static string ChangeOf(Presence at)
    {
        if (!at.Older)
        {
            return "added";
        }

        return at.Newer ? "changed" : "removed";
    }

    private static string? Note(params string?[] notes) =>
        notes.OfType<string>().ToArray() is { Length: > 0 } some ? string.Join("; ", some) : null;

    private readonly record struct Presence(bool Older, bool Newer, bool Project);
}

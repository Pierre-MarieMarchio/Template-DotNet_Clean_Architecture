namespace CleanArch.Cli.UpgradeReport;

internal sealed class TreeComparison
{
    private const string _solutionEditors = "presentation-scaffold and web-scaffold add the .NET projects they create to it";

    private readonly Trees _trees;
    private readonly HashSet<string> _olderFiles;
    private readonly HashSet<string> _newerFiles;
    private readonly HashSet<string> _held;
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
        _trees = trees;
        _olderFiles = new HashSet<string>(ProjectTree.Files(trees.Older), StringComparer.Ordinal);
        _newerFiles = new HashSet<string>(ProjectTree.Files(trees.Newer), StringComparer.Ordinal);
        _held = new HashSet<string>(projectFiles, StringComparer.Ordinal);
        _name = name;
        _joined = joined;
        _sameness = FileSameness.For(trees.Project, heldGuids);
        _scaffolds = ScaffoldedParts.In(trees.Older, trees.Newer, _olderFiles.Union(_newerFiles));
    }

    public static Comparison Compare(Trees trees, IReadOnlyCollection<string> projectFiles, string name, IReadOnlyDictionary<string, string> joined, IReadOnlySet<string>? heldGuids)
    {
        var comparison = new TreeComparison(trees, projectFiles, name, joined, heldGuids);

        foreach (string path in comparison._olderFiles.Union(comparison._newerFiles).Union(comparison._held).Order(StringComparer.Ordinal))
        {
            comparison.Classify(path);
        }

        return new Comparison(comparison._findings, comparison._bootstrap, comparison._scaffoldNotes, comparison._own, comparison._unchanged, comparison._provenance);
    }

    private void Classify(string path)
    {
        var at = new Presence(_olderFiles.Contains(path), _newerFiles.Contains(path), _held.Contains(path));

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

        paths.Add(path);
    }

    private bool Scaffolded(string path, string folder, Presence at)
    {
        string inside = path[(folder.Length + 1)..];

        if (inside == ScaffoldedParts.Descriptor)
        {
            if (!UnchangedByTheTarget(path, at))
            {
                _scaffoldNotes.Add($"{path}: {ChangeOf(at)} by the target version; the part was created from it, so take what the new descriptor brings by hand.");
            }

            return true;
        }

        if (inside.StartsWith("overlay/", StringComparison.Ordinal))
        {
            if (!UnchangedByTheTarget(path, at))
            {
                string target = folder + "/" + inside["overlay/".Length..];
                _findings.Add(new Finding(FindingKind.Merge, target, $"the part's scaffold laid {path} over the part it created", at.Older ? path : null, at.Newer ? path : null));
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
            _own.Add(path);
        }
    }

    private void Changed(string path, bool inProject)
    {
        string? edited = BootstrapWriters.Editor(path)
            ?? (ProjectTree.IsTheSolution(path, _name) && _scaffolds.AddToTheSolution ? _solutionEditors : null);
        string? join = _joined.TryGetValue(path, out string? part) ? $"the join of {part} edits it" : null;

        if (Same(_trees.Older, _trees.Newer, path))
        {
            _unchanged++;
        }
        else if (!inProject)
        {
            _findings.Add(new Finding(FindingKind.Merge, path, Note("the project deleted it", join), path, path));
        }
        else if (edited is null && join is null && Same(_trees.Older, _trees.Project, path))
        {
            _findings.Add(new Finding(FindingKind.Take, path, null, path, path));
        }
        else
        {
            _findings.Add(new Finding(FindingKind.Merge, path, Note(edited, join), path, path));
        }
    }

    private void Added(string path, bool inProject)
    {
        if (!inProject)
        {
            _findings.Add(new Finding(FindingKind.Added, path, null, null, path));
        }
        else if (Same(_trees.Newer, _trees.Project, path))
        {
            _unchanged++;
        }
        else
        {
            _findings.Add(new Finding(FindingKind.Added, path, "the project holds a file of its own at this path: merge by hand", null, path));
        }
    }

    private void Removed(string path, bool inProject)
    {
        if (!inProject)
        {
            _unchanged++;
            return;
        }

        string? note = Same(_trees.Older, _trees.Project, path) ? null : "the project changed it: decide by hand";
        _findings.Add(new Finding(FindingKind.Removed, path, note, path, null));
    }

    private bool UnchangedByTheTarget(string path, Presence at) => at.Older && at.Newer && Same(_trees.Older, _trees.Newer, path);

    private bool Same(string left, string right, string path) => _sameness.Same(Path.Combine(left, path), Path.Combine(right, path));

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

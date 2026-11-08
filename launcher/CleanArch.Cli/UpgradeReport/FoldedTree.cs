namespace CleanArch.Cli.UpgradeReport;

internal sealed class FoldedTree
{
    private static readonly string[] _roots = ["src", "tests", "tools"];

    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    public FoldedTree(string root, IEnumerable<string> paths)
    {
        Root = root;
        foreach (string path in paths)
        {
            _paths.TryAdd(Key(path), path);
        }
    }

    public string Root { get; }

    public IEnumerable<string> Keys => _paths.Keys;

    public bool Holds(string key) => _paths.ContainsKey(key);

    public string PathOf(string key) => _paths.GetValueOrDefault(key, key);

    public string Full(string key) => Path.Combine(Root, PathOf(key));

    public static string Key(string path)
    {
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        string first = slash < 0 ? path : path[..slash];
        string lower = first.ToLowerInvariant();

        return slash > 0 && first != lower && _roots.Contains(lower, StringComparer.Ordinal) ? lower + path[slash..] : path;
    }
}

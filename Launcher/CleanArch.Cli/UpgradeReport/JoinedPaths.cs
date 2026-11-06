using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class JoinedPaths
{
    private const string _joiningSection = "## Joining a project generated without one";

    private static readonly Regex _joinedPath = new(@"^- in `(?<path>[^`]+)`", RegexOptions.None, TimeSpan.FromSeconds(2));

    public static Dictionary<string, string> Of(string olderTree, string newerTree, GenerationPlan plan)
    {
        var joined = new Dictionary<string, string>(StringComparer.Ordinal);

        IEnumerable<(string Tree, string Folder)> parts = plan.Replays.SelectMany(replay =>
            new[] { olderTree, newerTree }.SelectMany(tree => ProjectTree.Resolve(tree, replay.Folder).Select(folder => (tree, folder))));
        foreach ((string tree, string folder) in parts)
        {
            foreach (string path in EditedBy(Path.Combine(tree, folder, "README.md")))
            {
                joined.TryAdd(path, folder);
            }
        }

        return joined;
    }

    private static IEnumerable<string> EditedBy(string readme)
    {
        string[] lines = File.Exists(readme) ? File.ReadAllLines(readme) : [];
        int start = Array.IndexOf(lines, _joiningSection);

        return start < 0
            ? []
            : lines.Skip(start + 1)
                .TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => _joinedPath.Match(line))
                .Where(edit => edit.Success)
                .Select(edit => edit.Groups["path"].Value);
    }
}

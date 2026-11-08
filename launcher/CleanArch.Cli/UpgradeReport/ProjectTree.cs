using CleanArch.Tooling.Failures;

namespace CleanArch.Cli.UpgradeReport;

internal static class ProjectTree
{
    private static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal)
    {
        ".git", ".vs", ".idea", ".claude", "bin", "obj", "node_modules", "TestResults", "artifacts",
    };

    public static IEnumerable<string> Files(string root)
    {
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string folder in Directory.EnumerateDirectories(directory).Where(folder => !_skippedFolders.Contains(Path.GetFileName(folder))))
            {
                pending.Push(folder);
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
            }
        }
    }

    public static string[] ProjectFiles(ReportProcesses processes, string root)
    {
        (int exit, string output, _) = processes.Capture(root, "git", "ls-files", "--cached", "--others", "--exclude-standard", "-z");

        return exit == 0
            ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => File.Exists(Path.Combine(root, path))).Order(StringComparer.Ordinal)]
            : [.. Files(root)];
    }

    public static bool IsTheSolution(string path, string name) => path == name + ".slnx" || path == name + ".sln";

    public static void RequireTheProjectsPaths(string olderTree, string root, IReadOnlyCollection<string> projectFiles, string name)
    {
        string[] generated = [.. Files(olderTree).Where(path => path.EndsWith(".csproj", StringComparison.Ordinal) || IsTheSolution(path, name))];
        var held = new HashSet<string>(projectFiles, StringComparer.Ordinal);

        if (!held.Any(path => IsTheSolution(path, name)) || !generated.Any(held.Contains))
        {
            string example = generated.FirstOrDefault(path => path.EndsWith(".csproj", StringComparison.Ordinal)) ?? name + ".slnx";
            throw new LauncherException(
                $"Generating '{name}', the name cleanarch.json records, writes paths this project does not hold, such as {example}, in {root}: "
                + "the project's name or its folders' initials are not the generation's, so every file would read as changed. Compare by hand.");
        }
    }

    public static IEnumerable<string> Resolve(string tree, string folder)
    {
        string parent = Path.Combine(tree, Path.GetDirectoryName(folder)!);

        return Directory.Exists(parent)
            ? Directory.EnumerateDirectories(parent, Path.GetFileName(folder)).Select(found => Path.GetRelativePath(tree, found).Replace('\\', '/'))
            : [];
    }
}

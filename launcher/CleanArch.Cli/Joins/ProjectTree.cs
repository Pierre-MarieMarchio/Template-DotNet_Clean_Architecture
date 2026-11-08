using System.Text;

namespace CleanArch.Cli.Joins;

internal static class ProjectTree
{
    private static readonly string[] _notInTheProject = [".git", "bin", "obj", "node_modules", "artifacts", "TestResults"];

    public static bool HasBom(string path) => File.ReadAllBytes(path) is [0xEF, 0xBB, 0xBF, ..];

    public static void Write(string path, string text, bool bom)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(bom));
    }

    public static void WriteLines(string root, string relative, List<string> lines)
    {
        string full = Path.Combine(root, relative);
        Write(full, string.Join('\n', lines), File.Exists(full) && HasBom(full));
    }

    public static IEnumerable<string> TextFiles(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(file => ProjectPath.Relative(root, file))
            .Where(path => !path.Split('/').SkipLast(1).Any(folder => _notInTheProject.Contains(folder, StringComparer.Ordinal)))
            .Where(path => !File.ReadAllBytes(Path.Combine(root, path)).Contains((byte)0));

    public static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(source).Where(folder => !_notInTheProject.Contains(Path.GetFileName(folder), StringComparer.Ordinal)))
        {
            Copy(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }

    public static void RemoveEmptyFolders(IEnumerable<string> folders)
    {
        foreach (string folder in folders.Distinct(StringComparer.Ordinal).OrderByDescending(folder => folder.Length))
        {
            for (string? empty = folder; empty is not null && Directory.Exists(empty) && !Directory.EnumerateFileSystemEntries(empty).Any(); empty = Path.GetDirectoryName(empty))
            {
                Directory.Delete(empty);
            }
        }
    }
}

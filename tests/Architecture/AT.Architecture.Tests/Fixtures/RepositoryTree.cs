namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class RepositoryTree
{
    internal static string Relative(string file) => Relative(ProjectReferenceGraph.RepositoryRoot, file);

    internal static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');

    internal static IEnumerable<string> SourceFiles(string root, string pattern, IReadOnlyCollection<string> skipped)
    {
        var pending = new Stack<string>([root]);

        while (pending.TryPop(out string? directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, pattern))
            {
                yield return file;
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!skipped.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }
}

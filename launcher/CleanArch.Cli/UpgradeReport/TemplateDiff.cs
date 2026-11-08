namespace CleanArch.Cli.UpgradeReport;

internal static class TemplateDiff
{
    public static string Of(ReportProcesses processes, string scratch, Trees trees, Finding finding)
    {
        (string older, string newer) = finding.Path.EndsWith(".sln", StringComparison.Ordinal) && finding.Older is not null && finding.Newer is not null
            ? CanonicalSolutions(scratch, trees, finding.Older, finding.Newer)
            : (trees.Older, trees.Newer);

        string Side(string tree, string? path) => path is null ? "/dev/null" : Path.GetRelativePath(scratch, Path.Combine(tree, path)).Replace('\\', '/');

        (int exit, string output, string errors) = processes.Capture(scratch, "git", "diff", "--no-index", "--no-color", "--no-prefix", "--", Side(older, finding.Older), Side(newer, finding.Newer));

        return exit is 0 or 1
            ? $"{finding.Path}:\n{output.TrimEnd()}"
            : $"{finding.Path}: no diff, since git diff --no-index failed: {errors.Trim()}";
    }

    private static (string Older, string Newer) CanonicalSolutions(string scratch, Trees trees, string olderPath, string newerPath)
    {
        string canonical = Path.Combine(scratch, "canonical");

        foreach ((string tree, string path) in new[] { (trees.Older, olderPath), (trees.Newer, newerPath) })
        {
            string copy = Path.Combine(canonical, Path.GetFileName(tree), path);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.WriteAllText(copy, CanonicalText.Solution(CanonicalText.Normalised(File.ReadAllBytes(Path.Combine(tree, path)))));
        }

        return (Path.Combine(canonical, Path.GetFileName(trees.Older)), Path.Combine(canonical, Path.GetFileName(trees.Newer)));
    }
}

using System.Text.RegularExpressions;

namespace CleanArch.Cli.Commands.Kernel;

internal static class TestProjects
{
    private static readonly Regex _windowsTargetFramework = new(
        @"<TargetFramework>\s*(?<framework>net\d+(\.\d+)*-windows(\d+(\.\d+)*)?)\s*</TargetFramework>",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    public static string[] All(string root)
    {
        string tests = Path.Combine(root, "Tests");
        IEnumerable<string> projects = Directory.Exists(tests)
            ? Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)
            : [];
        string[] paths = [.. projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("Discovery found no test project under Tests/. A run over nothing is a false green.");
        }

        return paths;
    }

    public static bool IsDockerFree(string projectText) =>
        projectText.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal)
        && !projectText.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase)
        && !projectText.Contains("Aspire.Hosting.Testing", StringComparison.OrdinalIgnoreCase);

    public static string[] SkippedHere(string[] tests) =>
        OperatingSystem.IsWindows() ? [] : [.. tests.Where(path => WindowsFramework(File.ReadAllText(path)) is not null)];

    public static string? WindowsFramework(string projectText) =>
        _windowsTargetFramework.Match(projectText) is { Success: true } match ? match.Groups["framework"].Value : null;

    public static string NotRunLine(string root, string test) => $"Built, not run {FolderOf(root, test)}: it needs Docker.";

    public static string SkipLine(string root, string test) =>
        $"Skipped {FolderOf(root, test)}: it targets {WindowsFramework(File.ReadAllText(test))} and runs on Windows alone.";

    private static string FolderOf(string root, string test) =>
        Path.GetRelativePath(root, Path.GetDirectoryName(test)!).Replace('\\', '/');
}

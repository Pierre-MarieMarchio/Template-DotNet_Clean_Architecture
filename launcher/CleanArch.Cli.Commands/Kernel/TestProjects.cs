using System.Text.RegularExpressions;

namespace CleanArch.Cli.Commands.Kernel;

internal static class TestProjects
{
    public const string NoneFound = "Discovery found no test project under tests/. A run over nothing is a false green.";

    private static readonly Regex _windowsTargetFramework = new(
        @"<TargetFramework>\s*(?<framework>net\d+(\.\d+)*-windows(\d+(\.\d+)*)?)\s*</TargetFramework>",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    public static string[] All(string root)
    {
        string[] paths = Under(root);
        return paths.Length > 0 ? paths : throw new InvalidOperationException(NoneFound);
    }

    public static string[] Under(string root)
    {
        string tests = Path.Combine(root, "tests");
        IEnumerable<string> projects = Directory.Exists(tests)
            ? Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)
            : [];
        return [.. projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }

    public static bool IsDockerFree(string test)
    {
        string projectText = File.ReadAllText(test);
        return projectText.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal)
            && !projectText.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase)
            && !projectText.Contains("Aspire.Hosting.Testing", StringComparison.OrdinalIgnoreCase);
    }

    public static string[] SkippedHere(string[] tests) =>
        OperatingSystem.IsWindows() ? [] : [.. tests.Where(path => WindowsFramework(path) is not null)];

    public static string NotRunLine(string root, string test) => $"Built, not run {FolderOf(root, test)}: it needs Docker.";

    public static string SkipLine(string root, string test) =>
        $"Skipped {FolderOf(root, test)}: it targets {WindowsFramework(test)} and runs on Windows alone.";

    public static string? WindowsFramework(string test) =>
        _windowsTargetFramework.Match(File.ReadAllText(test)) is { Success: true } match ? match.Groups["framework"].Value : null;

    private static string FolderOf(string root, string test) => ProjectPath.Relative(root, Path.GetDirectoryName(test)!);
}

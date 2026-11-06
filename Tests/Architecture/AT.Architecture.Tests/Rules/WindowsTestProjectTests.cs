using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class WindowsTestProjectTests
{
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(2);

    private static readonly string[] _skippedFolders = ["bin", "obj", "node_modules", ".git", ".claude", "artifacts", "TestResults"];

    private static readonly Regex _taskPattern = new(
        @"_windowsTargetFramework = new\(\s*@""(?<pattern>(?:[^""]|"""")*)""", RegexOptions.None, _budget);

    private static readonly Regex _namesWindows = new(
        @"-windows|<UseWPF>\s*true|<UseWindowsForms>\s*true", RegexOptions.IgnoreCase, _budget);

    private static readonly Regex _severalFrameworks = new(@"<TargetFrameworks>", RegexOptions.None, _budget);

    private static readonly Regex _windowsFrameworkInBuildFile = new(
        @"<TargetFrameworks?>[^<]*windows", RegexOptions.IgnoreCase, _budget);

    [Fact]
    public void ATestProjectForWindows_SaysSoInTheTargetFrameworkOfItsOwnProjectFile()
    {
        Regex windowsOnly = TasksWindowsTargetFramework();
        TestProject[] tests = [.. TestProjects()];

        tests.Length.ShouldBeGreaterThanOrEqualTo(5, "Far fewer test projects were found than this project holds, so this rule is not reading its tree.");

        tests
            .Where(test => (_namesWindows.IsMatch(test.Text) || test.References.Any(IsWindowsProject)) && !windowsOnly.IsMatch(test.Text))
            .Select(test => test.Path)
            .ShouldBeEmpty(
                "Off Windows, the test, check, coverage and verify tasks leave out a test project by reading <TargetFramework> in its own "
                + "project file, as Tools/Tasks.cs's _windowsTargetFramework matches it. A test project that targets Windows, or references a "
                + "project that does, declares <TargetFramework>net10.0-windows</TargetFramework> itself; otherwise the tasks run it where "
                + "it cannot run.");
        tests
            .Where(test => _severalFrameworks.IsMatch(test.Text))
            .Select(test => test.Path)
            .ShouldBeEmpty("A test project names one <TargetFramework>, which the tasks read: <TargetFrameworks> hides a Windows target from them.");
    }

    [Fact]
    public void NoBuildFile_SetsAWindowsTargetFramework()
    {
        string[] buildFiles = [.. RepositoryTree.SourceFiles(ProjectReferenceGraph.RepositoryRoot, "*", _skippedFolders)
            .Where(file => Path.GetExtension(file) is ".props" or ".targets")];

        buildFiles.ShouldNotBeEmpty("Directory.Build.props sets the target framework of every project; a search that finds no build file is broken.");
        buildFiles
            .Where(file => _windowsFrameworkInBuildFile.IsMatch(File.ReadAllText(file)) || _severalFrameworks.IsMatch(File.ReadAllText(file)))
            .Select(RepositoryTree.Relative)
            .ShouldBeEmpty(
                "A Windows target framework set by a .props or .targets file is one the tasks cannot see when they read a test project's own "
                + "file: set it in the project file of the Windows project and of its tests.");
    }

    [Fact]
    public void TheTasksMatcher_KnowsAWindowsTargetFrameworkFromAnother()
    {
        Regex windowsOnly = TasksWindowsTargetFramework();

        windowsOnly.IsMatch("<TargetFramework>net10.0-windows</TargetFramework>").ShouldBeTrue();
        windowsOnly.IsMatch("<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>").ShouldBeTrue();
        windowsOnly.IsMatch("<TargetFramework>net10.0</TargetFramework>").ShouldBeFalse();
        windowsOnly.IsMatch("<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>").ShouldBeFalse(
            "the tasks read one target framework, which is why a test project may not name several");
        _namesWindows.IsMatch("<UseWPF>true</UseWPF>").ShouldBeTrue();
        _windowsFrameworkInBuildFile.IsMatch("<TargetFramework>net10.0-windows</TargetFramework>").ShouldBeTrue();
        _windowsFrameworkInBuildFile.IsMatch("<TargetFramework>net10.0</TargetFramework>").ShouldBeFalse();
    }

    private sealed record TestProject(string Path, string Text, IReadOnlyList<string> References);

    private static Regex TasksWindowsTargetFramework()
    {
        string tasks = File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Tools", "Tasks.cs"));
        Match declared = _taskPattern.Match(tasks);

        declared.Success.ShouldBeTrue("Tools/Tasks.cs declares no _windowsTargetFramework, the matcher by which the tasks know a Windows-only test project.");

        return new Regex(declared.Groups["pattern"].Value.Replace("\"\"", "\"", StringComparison.Ordinal), RegexOptions.ExplicitCapture, _budget);
    }

    private static IEnumerable<TestProject> TestProjects() =>
        TestRoots()
            .SelectMany(root => RepositoryTree.SourceFiles(root, "*", _skippedFolders))
            .Where(file => file.EndsWith(".csproj", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(file =>
            {
                string text = File.ReadAllText(file);
                string folder = Path.GetDirectoryName(file)!;
                string[] references = [.. XDocument.Parse(text).Descendants("ProjectReference")
                    .Select(reference => reference.Attribute("Include")?.Value)
                    .OfType<string>()
                    .Select(include => Path.GetFullPath(Path.Combine(folder, include.Replace('\\', '/'))))];

                return new TestProject(RepositoryTree.Relative(file), text, references);
            });

    private static IEnumerable<string> TestRoots()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string starters = Path.Combine(root, ".template.config", "starters");
        IEnumerable<string> starterTests = Directory.Exists(starters)
            ? Directory.EnumerateDirectories(starters).Select(starter => Path.Combine(starter, "Tests"))
            : [];

        return new[] { Path.Combine(root, "Tests") }.Concat(starterTests).Where(Directory.Exists);
    }

    private static bool IsWindowsProject(string projectFile) =>
        File.Exists(projectFile) && _namesWindows.IsMatch(File.ReadAllText(projectFile));
}

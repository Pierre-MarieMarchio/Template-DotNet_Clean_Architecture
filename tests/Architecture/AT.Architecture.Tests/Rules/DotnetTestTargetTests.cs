using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class DotnetTestTargetTests
{
    private static readonly TimeSpan _budget = TimeSpan.FromSeconds(2);

    private static readonly string[] _extensions = [".cs", ".md", ".yml", ".yaml", ".sh", ".ps1"];

    private static readonly string[] _skippedFolders = ["bin", "obj", "node_modules", ".git", ".claude", "tests", "plans", "artifacts"];

    private static readonly Regex _shellCommand = new(
        @"dotnet\s+test(?:[ \t]*\\\r?\n)?[ \t]+(?<target>[^\s`]+)", RegexOptions.None, _budget);

    private static readonly Regex _taskStep = new(
        @"""dotnet""\s*,\s*\[?\s*""test""\s*,\s*(?<target>[^\s,)\]]+)", RegexOptions.None, _budget);

    private static readonly Regex _aTarget = new(
        @"^(?:[""'$.{]|[^\s]*[/\\]|[^\s]*\.(?:sln|slnx|slnf|csproj)\b)", RegexOptions.None, _budget);

    [Fact]
    public void EveryDotnetTest_NamesItsTargetBySolutionOrProject()
    {
        var files = Sources().ToList();

        files.ShouldNotBeEmpty("A search of the repository that finds no source is broken.");
        files
            .SelectMany(Violations)
            .ShouldBeEmpty(
                "Microsoft.Testing.Platform takes the solution as --solution <path> and a project as --project <path>: a newer SDK "
                + "refuses the positional path with \"Specifying a solution for 'dotnet test' should be via '--solution'\", and "
                + "10.0.100, which CI installs, still accepts it, so only a machine with a current SDK sees the failure.");
    }

    [Fact]
    public void TheMatchers_AreSensitive()
    {
        Found(_shellCommand, "dotnet test \"$project\" --no-build").ShouldBe("\"$project\"");
        Found(_shellCommand, "dotnet test tests/Unit/A.csproj").ShouldBe("tests/Unit/A.csproj");
        Found(_shellCommand, "dotnet test \\\n  \"$SOLUTION\" \\").ShouldBe("\"$SOLUTION\"");
        Found(_shellCommand, "dotnet test --solution \"$SOLUTION\"").ShouldBe("--solution");
        Found(_shellCommand, "so `dotnet test` over the solution").ShouldBeNull("the closing backtick ends the command");
        Found(_taskStep, "Step(\"dotnet\", [\"test\", solution, \"--no-build\"]);").ShouldBe("solution");
        Found(_taskStep, "\"dotnet\",\n    [\n        \"test\", solution,").ShouldBe("solution");
        Found(_taskStep, "Step(\"dotnet\", \"test\", \"--solution\", solution);").ShouldBe("\"--solution\"");
        Found(_taskStep, "string[] scripts = [\"lint\", \"test\", \"build\"];").ShouldBeNull("only a dotnet step is judged");

        IsPositional("--project").ShouldBeFalse();
        IsPositional("\"--solution\"").ShouldBeFalse();
        IsPositional("\"$project\"").ShouldBeTrue();
        IsPositional("services/api/App.sln").ShouldBeTrue();
        IsPositional("App.slnx").ShouldBeTrue();
        IsPositional("solution").ShouldBeTrue("a variable of a task step is a path");
        _aTarget.IsMatch("reports").ShouldBeFalse("a word of prose is not a path");
    }

    private static IEnumerable<string> Violations(string file)
    {
        string text = File.ReadAllText(file);
        string relative = RepositoryTree.Relative(file);

        return Matches(_shellCommand, text)
            .Where(target => _aTarget.IsMatch(target) && IsPositional(target))
            .Concat(Matches(_taskStep, text).Where(IsPositional))
            .Select(target => $"{relative}: dotnet test {target}");
    }

    private static IEnumerable<string> Matches(Regex matcher, string text) =>
        matcher.Matches(text).Select(match => match.Groups["target"].Value);

    private static string? Found(Regex matcher, string text) =>
        matcher.Match(text) is { Success: true } match ? match.Groups["target"].Value : null;

    private static bool IsPositional(string target) =>
        !target.TrimStart('"').StartsWith("--", StringComparison.Ordinal);

    private static IEnumerable<string> Sources() =>
        RepositoryTree.SourceFiles(ProjectReferenceGraph.RepositoryRoot, "*", _skippedFolders)
            .Where(file => _extensions.Contains(Path.GetExtension(file)))
            .Where(file => Path.GetFileName(file) != "CHANGELOG.md")
            .Order(StringComparer.Ordinal);
}

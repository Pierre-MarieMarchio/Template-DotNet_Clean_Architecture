using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ClockUsageTests
{
    private static readonly Regex _ambientClock = new(
        @"\bDateTime(?:Offset)?\s*\.\s*(?:UtcNow|Now|Today)\b",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _commentLine = new(
        @"^\s*(?://|///).*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void NoSourceFile_ReadsTheMachinesClockDirectly()
    {
        var sources = ProductionSources();

        sources.Count.ShouldBeGreaterThanOrEqualTo(
            100,
            "Far fewer source files were found under Src than this template ships, so this rule is " +
            "reading the wrong tree and passing for the wrong reason.");

        var offenders = sources
            .Where(file => _ambientClock.IsMatch(_commentLine.Replace(File.ReadAllText(file), string.Empty)))
            .Select(RepositoryTree.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "Code under Src reads time through an injected TimeProvider (GetUtcNow), never through " +
            "DateTime or DateTimeOffset. A direct read cannot be moved by a test, so what depends on " +
            "it can only be tested by waiting.");
    }

    [Fact]
    public void TheClockRule_IsSensitive_AndSeesWhatItForbids()
    {
        _ambientClock.IsMatch("var now = DateTimeOffset.UtcNow;").ShouldBeTrue();
        _ambientClock.IsMatch("var now = DateTime.UtcNow;").ShouldBeTrue();
        _ambientClock.IsMatch("var now = DateTime.Now;").ShouldBeTrue();
        _ambientClock.IsMatch("var now = DateTimeOffset.Now;").ShouldBeTrue();
        _ambientClock.IsMatch("var day = DateTime.Today;").ShouldBeTrue();
        _ambientClock.IsMatch("var now = timeProvider.GetUtcNow();").ShouldBeFalse();
        _ambientClock.IsMatch("var epoch = DateTimeOffset.UnixEpoch;").ShouldBeFalse();
    }

    private static List<string> ProductionSources() =>
        [.. RepositoryTree.SourceFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src"), "*.cs", ["bin", "obj", "node_modules"])];
}

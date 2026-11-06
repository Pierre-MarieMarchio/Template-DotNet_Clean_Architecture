using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class SynchronousSaveTests
{
    private static readonly Regex _synchronousSave = new(
        @"\bSaveChanges\s*\(",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _commentLine = new(
        @"^\s*(?://|///).*$",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void NoSourceFile_CallsTheSynchronousSaveChanges()
    {
        var sources = ProductionSources();

        sources.Count.ShouldBeGreaterThanOrEqualTo(
            100,
            "Far fewer source files were found under Src than this template ships, so this rule is " +
            "reading the wrong tree and passing for the wrong reason.");

        var offenders = sources
            .Where(file => _synchronousSave.IsMatch(_commentLine.Replace(File.ReadAllText(file), string.Empty)))
            .Select(RepositoryTree.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "Code under Src saves through SaveChangesAsync, never through SaveChanges: the synchronous call " +
            "blocks the request's thread on a database round trip.");
    }

    [Fact]
    public void TheSaveRule_IsSensitive_AndSeesWhatItForbids()
    {
        _synchronousSave.IsMatch("own.SaveChanges();").ShouldBeTrue();
        _synchronousSave.IsMatch("context.SaveChanges ();").ShouldBeTrue();
        _synchronousSave.IsMatch("await own.SaveChangesAsync(CancellationToken.None);").ShouldBeFalse();
        _synchronousSave.IsMatch("public override InterceptionResult<int> SavingChanges(DbContextEventData data)").ShouldBeFalse();
        _synchronousSave.IsMatch("public override int SavedChanges(SaveChangesCompletedEventData data, int result)").ShouldBeFalse();
    }

    private static List<string> ProductionSources() =>
        [.. RepositoryTree.SourceFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src"), "*.cs", ["bin", "obj", "node_modules"])];
}

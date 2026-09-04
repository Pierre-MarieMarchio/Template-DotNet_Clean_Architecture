using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The layout itself, read from the source tree rather than from compiled metadata.
/// <para>
/// Reflection cannot see either of these rules. It knows a type's namespace but not which file
/// declares it, and it knows nothing at all about a folder that exists and holds nothing. Both are
/// properties of the tree a reader navigates, so the tree is what these read.
/// </para>
/// </summary>
public sealed class LayoutConventionTests
{
    /// <summary>
    /// The folders a feature may hold, per layer. Closed on purpose: a word outside this list is a
    /// concept a reader has to infer from its contents, and one feature inventing a word the others
    /// do not use is how a layout stops being an index. Adding one is an ADR
    /// (docs/adr/0025-closed-folder-vocabulary-per-layer.md), not a mkdir.
    /// </summary>
    private static readonly Dictionary<string, string[]> _vocabulary = new(StringComparer.Ordinal)
    {
        ["Src/Application/AppTemplate.Application"] =
            ["Consumers", "Dtos", "Errors", "Extensions", "Mapping", "Policies", "Ports", "Services", "UseCases"],
        ["Src/Domain/AppTemplate.Domain"] =
            ["Entities", "Events", "Repositories", "ValueObjects"],
        ["Src/Infrastructure/AppTemplate.Infrastructure.Persistence"] =
            ["Configurations", "Mapping", "Models", "Queries", "Repositories", "Seeding", "Stores", "Tracking"],
        ["Src/Presentation/AppTemplate.Api"] =
            ["Contracts", "Controllers", "Mapping"],
    };

    /// <summary>
    /// A top-level public type declaration. Anchored at column zero because a nested type is indented
    /// and stays with its parent, and because <c>internal</c> companions — an options validator beside
    /// the options it validates — are deliberately not the subject of the rule.
    /// </summary>
    private static readonly Regex _publicTypeDeclaration = new(
        @"^public\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*(?:class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryFeatureFolder_IsNamedFromItsLayersVocabulary()
    {
        var checkedLayers = 0;
        var offenders = new List<string>();

        foreach ((string project, string[] allowed) in _vocabulary)
        {
            string features = Path.Combine(ProjectReferenceGraph.RepositoryRoot, project, "Features");

            if (!Directory.Exists(features))
            {
                continue;
            }

            checkedLayers++;

            offenders.AddRange(Directory
                .EnumerateDirectories(features)
                .SelectMany(feature => Directory.EnumerateDirectories(feature))
                .Where(folder => !allowed.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
                .Select(folder => $"{project}: '{Path.GetRelativePath(features, folder)}' is not one of " +
                    $"[{string.Join(", ", allowed)}]"));
        }

        checkedLayers.ShouldBe(
            _vocabulary.Count,
            "A layer's Features folder was not found, so its vocabulary was never checked.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A folder outside its layer's vocabulary makes the reader infer a concept from the files " +
            "inside it, and lets one feature be organised unlike every other.");
    }

    /// <summary>
    /// A folder holding nothing is structure carrying no information: it suggests a concept the
    /// feature does not actually have, and a reader opening it learns only that they were misled.
    /// </summary>
    [Fact]
    public void NoFolderInTheSourceTree_IsEmpty()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var empty = Directory
            .EnumerateDirectories(source, "*", SearchOption.AllDirectories)
            .Where(folder => !IsBuildOutput(folder))
            .Where(folder => !Directory.EnumerateFileSystemEntries(folder).Any())
            .Select(folder => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, folder))
            .Order(StringComparer.Ordinal)
            .ToList();

        empty.ShouldBeEmpty("A folder exists only when it has contents.");
    }

    /// <summary>
    /// One top-level public type per file, and the file is named for it — which is what makes a type
    /// findable by its name alone, without a search across a tree of similar folders.
    /// <para>
    /// Generic overloads of one name (<c>IUseCase</c>, <c>IUseCase&lt;T&gt;</c>,
    /// <c>IUseCase&lt;T,R&gt;</c>) count once: they are one concept, and the arity is not part of what
    /// a reader looks for.
    /// </para>
    /// </summary>
    [Fact]
    public void EverySourceFile_DeclaresOnePublicType_NamedForTheFile()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var files = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => !IsGenerated(file))
            .ToList();

        files.Count.ShouldBeGreaterThanOrEqualTo(
            300,
            "Far fewer source files were found than this template holds, so the walk is not reading " +
            "the tree it is meant to describe.");

        var offenders = new List<string>();

        foreach (string file in files)
        {
            var declared = PublicTypesIn(file);

            if (declared.Count == 0)
            {
                continue;
            }

            string relative = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);
            string expected = Path.GetFileNameWithoutExtension(file);

            if (declared.Count > 1)
            {
                offenders.Add(
                    $"{relative} declares {declared.Count} public types " +
                    $"({string.Join(", ", declared.Order(StringComparer.Ordinal))}).");
            }
            else if (!declared.Contains(expected))
            {
                offenders.Add($"{relative} declares '{declared.First()}'.");
            }
        }

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A file holds one public type and is named for it. A second one hides behind a name that " +
            "does not mention it, and a mismatched name makes the type unfindable by the only thing a " +
            "reader knows about it.");
    }

    /// <summary>
    /// Proves the file walk above can fail, by running its predicate over a file written to break
    /// the rule. Without this, a walk that silently matched nothing would report no offenders and
    /// read as a pass.
    /// </summary>
    [Fact]
    public void TheOneTypePerFileRule_IsSensitive_AndSeesASecondType()
    {
        string probe = Path.Combine(Path.GetTempPath(), $"{nameof(LayoutConventionTests)}.probe.cs");

        File.WriteAllText(
            probe,
            """
            namespace Probe;

            public sealed class First
            {
                public sealed class Nested;
            }

            public sealed record Second(string Value);
            """);

        try
        {
            var declared = PublicTypesIn(probe);

            declared.ShouldBe(
                ["First", "Second"],
                ignoreOrder: true,
                "The predicate must see both top-level types and neither the nested one nor anything " +
                "else. Seeing one would make the real rule vacuous; seeing three would make it noisy.");
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static HashSet<string> PublicTypesIn(string file) =>
        [.. _publicTypeDeclaration
            .Matches(File.ReadAllText(file))
            .Select(match => match.Groups[1].Value)];

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// EF Core writes migrations, and their shape is the tool's to decide, not this template's.
    /// </summary>
    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}

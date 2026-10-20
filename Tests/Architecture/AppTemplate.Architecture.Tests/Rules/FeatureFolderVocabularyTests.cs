using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class FeatureFolderVocabularyTests
{
    private static readonly HashSet<string> _concepts = new(StringComparer.Ordinal)
    {
        "Access", "Adapters", "Configuration", "Model", "Resources", "Rows", "Services",
    };

    private static readonly Dictionary<string, string[]> _suffixes = new(StringComparer.Ordinal)
    {
        ["Adapters/Services"] = ["Service", "Factory", "Issuer", "Log", "Verifier"],
        ["Checkpoints"] = ["Checkpoint"],
        ["Composition"] = ["Composition", "Directory", "Provider", "Session", "Mapper", "Mapping"],
        ["Configurations"] = ["Configuration"],
        ["Consumers"] = ["Consumer"],
        ["Contracts"] = ["Request", "Response", "Mapping"],
        ["Controllers"] = ["Controller"],
        ["Directories"] = ["Directory"],
        ["Dtos"] = ["Dto", "Validator"],
        ["Entities"] = [],
        ["Errors"] = ["Errors"],
        ["Events"] = ["Event"],
        ["Helpers"] = ["Helpers", "Extensions"],
        ["Inventories"] = ["Inventory"],
        ["Jobs"] = ["BackgroundService", "Extensions", "Instruments", "Module", "Options", "Task", "Telemetry"],
        ["Locks"] = ["Lock"],
        ["Mapping"] = ["Mapper", "Mapping"],
        ["Models"] = ["Record"],
        ["Observability"] = ["Instruments", "Diagnostics"],
        ["Options"] = ["Options"],
        ["Policies"] = ["Policy"],
        ["Ports"] = [],
        ["Providers"] = ["Provider", "ProviderName"],
        ["Queries"] = ["Queries", "Query", "Map", "Pattern"],
        ["Repositories"] = ["Repository"],
        ["Scanners"] = ["Scanner"],
        ["Seeding"] = ["Seeder", "Options", "Roles"],
        ["Services"] = ["Service"],
        ["Stores"] = ["Store"],
        ["Tables"] = ["Table", "Grant"],
        ["Templates"] = [],
        ["Tracking"] = ["Tracker"],
        ["Transfers"] = ["Transfer"],
        ["UseCases"] = [],
        ["ValueObjects"] = [],
    };

    private static readonly Dictionary<string, string> _exempt = new(StringComparer.Ordinal)
    {
        //#if (files)
        ["ContentDecision.cs"] = "the decision StoredFileContentPolicy returns",
        //#endif
        //#if (localAuth)
        ["ExternalAccountLinkDecision.cs"] = "the decision the external-account policy returns",
        //#endif
        //#if (files)
        ["MediaTypeSignatures.cs"] = "the magic-byte table StoredFileContentPolicy reads",
        //#endif
        //#if (localAuth)
        ["AppUser.cs"] = "ASP.NET Core Identity's own user row",
        ["AppRole.cs"] = "ASP.NET Core Identity's own role row",
        ["RefreshToken.cs"] = "the row IRefreshTokenTable reads and writes",
        ["CachedSigningKeys.cs"] = "the cache SigningKeyDirectory reads and fills",
        //#endif
    };

    [Fact]
    public void EveryFileUnderAFeature_CarriesItsFoldersNatureWord()
    {
        var offenders = new List<string>();
        int checkedFiles = 0;

        foreach ((string folder, string relative, string file) in FeatureFiles())
        {
            if (!TrySuffixesOf(folder, out string[]? suffixes))
            {
                continue;
            }

            checkedFiles++;

            if (suffixes.Length == 0 || _exempt.ContainsKey(file))
            {
                continue;
            }

            string stem = Path.GetFileNameWithoutExtension(file);

            if (!suffixes.Any(suffix => stem.EndsWith(suffix, StringComparison.Ordinal)))
            {
                offenders.Add(
                    $"{relative} sits in '{folder}/', whose files are named [{string.Join(", ", suffixes)}]");
            }
        }

        checkedFiles.ShouldBeGreaterThan(
            8,
            "Far fewer feature files were found than this template holds, so the walk is not reading "
            + "the tree it is meant to describe and this rule would pass on almost nothing.");

        offenders.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A folder under Features/<F>/ is the plural of the nature word its files carry, so that a "
            + "type name tells you its folder and a folder tells you what is in it. Rename the type, "
            + "move it to the folder its word names, or — if it is a type in another thing's "
            + "signature — add it to the exemption list with the reason.");
    }

    [Fact]
    public void EveryFeatureFolder_IsCovered()
    {
        var undescribed = FeatureFiles()
            .Select(found => found.Folder)
            .Where(folder => !TrySuffixesOf(folder, out _))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        undescribed.ShouldBeEmpty(
            "These folders exist under Features/ and no entry here says what nature of thing they "
            + "hold, so nothing checks their contents.");
    }

    [Fact]
    public void EveryExemption_StillNamesAFile()
    {
        var present = FeatureFiles().Select(found => found.File).ToHashSet(StringComparer.Ordinal);

        var stale = _exempt.Keys
            .Where(file => !present.Contains(file))
            .Order(StringComparer.Ordinal)
            .ToList();

        stale.ShouldBeEmpty("These exemptions name files that are no longer under Features/.");
    }

    private static readonly Regex _topLevelTypeDeclaration = new(
        @"^((?:(?:public|internal|file|sealed|abstract|static|partial|readonly|ref|unsafe)\s+)*)(class|record|struct|interface|enum|delegate)\b[^\r\n:{;(]*",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryTypeUnderAHelpersFolder_IsAStaticClass()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var offenders = Directory
            .EnumerateDirectories(source, "Helpers", SearchOption.AllDirectories)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .SelectMany(file => NonStaticDeclarationsIn(File.ReadAllText(file))
                .Select(declaration =>
                    $"{Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file)}: '{declaration}'"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A type under Helpers/ is a static class of pure functions, never injected and never a "
            + "contract. One with state or a dependency belongs in Services/ or an adapter, a rule "
            + "that protects a state in Model/Policies/, and one with a more precise nature in that "
            + "nature's folder.");
    }

    [Fact]
    public void TheHelpersRule_IsSensitive_AndSeesAnyTypeButAStaticClass()
    {
        string probe = """
            namespace Probe;

            public static class TagHelpers
            {
                public sealed class Nested;
            }

            internal static partial class TodoItemExtensions;

            public sealed class TagFormatter : ITagFormatter;

            public record TagSummary(string Value);

            public interface ITagFormatter;
            """;

        NonStaticDeclarationsIn(probe).ShouldBe(
            ["public sealed class TagFormatter", "public record TagSummary", "public interface ITagFormatter"],
            ignoreOrder: true,
            "The predicate must flag every top-level type that is not a static class, and neither the "
            + "static classes nor the type nested in one.");
    }

    private static IEnumerable<string> NonStaticDeclarationsIn(string text) =>
        _topLevelTypeDeclaration
            .Matches(text)
            .Where(match => match.Groups[2].Value is not "class"
                || !match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("static"))
            .Select(match => match.Value.Trim());

    private static bool TrySuffixesOf(string folder, [NotNullWhen(true)] out string[]? suffixes) =>
        _suffixes.TryGetValue(folder, out suffixes)
        || _suffixes.TryGetValue(folder[(folder.IndexOf('/', StringComparison.Ordinal) + 1)..], out suffixes);

    private static IEnumerable<(string Folder, string Relative, string File)> FeatureFiles()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        foreach (string project in Directory.EnumerateDirectories(Path.Combine(root, "Src"), "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(project) != "Features")
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            {
                string[] parts = Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar);

                if (parts.Length < 3)
                {
                    continue;
                }

                string folder = parts.Length >= 4 && _concepts.Contains(parts[1])
                    ? $"{parts[1]}/{parts[2]}"
                    : parts[1];

                yield return (folder, Path.GetRelativePath(root, file), Path.GetFileName(file));
            }
        }
    }
}

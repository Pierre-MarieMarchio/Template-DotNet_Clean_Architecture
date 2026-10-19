using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// <c>docs/PROJECT-LAYOUT.md</c> says a folder under <c>Features/&lt;F&gt;/</c> is the plural of the nature
/// word its files carry — a <c>…Repository</c> in <c>Repositories/</c>, a <c>…Mapper</c> in
/// <c>Mapping/</c> — and that is what makes the tree navigable in both directions: a type name tells
/// you its folder, and a folder tells you what nature of thing is in it.
/// <para>
/// <c>LayoutConventionTests</c> holds the folder <em>names</em> to a closed list. Nothing held the
/// files to them, which is how a <c>…Access</c> came to sit in <c>Services/</c> and stay there: the
/// folder was legal, the type was reasonable, and the pair was the defect.
/// </para>
/// <para>
/// Only <c>Features/</c> is checked. A <c>Common/&lt;Responsibility&gt;/</c> folder is named for a
/// responsibility rather than a nature — <c>Errors/</c> holds a mapper, a handler and a normaliser,
/// and is right to.
/// </para>
/// </summary>
public sealed class FeatureFolderVocabularyTests
{
    /// <summary>
    /// The concepts whose subfolders are natures: under <c>Features/&lt;F&gt;/Model/Dtos/</c> the
    /// nature is <c>Dtos</c>, read at level 3. Every other level-2 folder is its own nature. A file
    /// directly under one of these is read against the concept itself, which only <c>Services</c>
    /// has suffixes for — under the others it is reported as undescribed.
    /// </summary>
    private static readonly HashSet<string> _concepts = new(StringComparer.Ordinal)
    {
        "Access", "Adapters", "Configuration", "Model", "Resources", "Rows", "Services",
    };

    /// <summary>
    /// The suffix each nature folder's files carry, keyed by the nature, or by
    /// <c>Concept/Nature</c> where one concept widens it. A folder absent from here is one no rule
    /// checks, which <see cref="EveryFeatureFolder_IsCovered"/> refuses.
    /// </summary>
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
        ["Extensions"] = ["Extensions"],
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
        ["Security"] = ["Session", "Extensions"],
        ["Seeding"] = ["Seeder", "Options", "Roles"],
        ["Services"] = ["Service"],
        ["Stores"] = ["Store"],
        ["Tables"] = ["Table", "Grant"],
        ["Templates"] = [],
        ["Tracking"] = ["Tracker"],
        ["Transfers"] = ["Transfer"],
        ["UseCases"] = [],
        ["Validators"] = ["Validator"],
        ["ValueObjects"] = [],
    };

    /// <summary>
    /// Files whose name cannot carry their folder's word, each for a reason that is not "we could
    /// not think of a better name". A list that grows without an argument in its pull request is the
    /// rule being dismantled one entry at a time.
    /// </summary>
    private static readonly Dictionary<string, string> _exempt = new(StringComparer.Ordinal)
    {
        // The value a policy hands back. It lives beside the policy that produces it for the same
        // reason a port's messages live beside the port: a type in the signature does not move away
        // from the thing whose signature it is.
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

        // ASP.NET Core Identity names these, not this repository: they are what IdentityUser<Guid>
        // and IdentityRole<Guid> are subclassed as, and a …Record suffix on them would be this
        // template renaming a framework's vocabulary in its own tree.
        ["AppUser.cs"] = "ASP.NET Core Identity's own user row",
        ["AppRole.cs"] = "ASP.NET Core Identity's own role row",
        ["RefreshToken.cs"] = "the row IRefreshTokenTable reads and writes",

        // The state SigningKeyDirectory holds, not a second directory — the same shape as
        // RecordedEmails and StoredObjects, which sit where no nature word applies at all.
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
                // Reported by EveryFeatureFolder_IsCovered, and reporting it twice would make one
                // omission look like two.
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

    /// <summary>
    /// The rule cannot check a folder nobody described, and a dictionary that iterates itself says
    /// nothing about what it omits. A new feature folder therefore fails here until its word is
    /// written down.
    /// </summary>
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

    /// <summary>
    /// An exemption for a file that is gone is an argument nobody can check any more, and it would
    /// silently cover a future file that happened to take the same name.
    /// </summary>
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

    /// <summary>
    /// A top-level type declaration, any accessibility, up to its name. Top-level types sit at
    /// column zero, the namespaces being file-scoped; the modifiers are captured to tell a static
    /// class apart.
    /// </summary>
    private static readonly Regex _topLevelTypeDeclaration = new(
        @"^((?:(?:public|internal|file|sealed|abstract|static|partial|readonly|ref|unsafe)\s+)*)(class|record|struct|interface|enum|delegate)\b[^\r\n:{;(]*",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// <c>Helpers/</c> holds pure static functions and nothing else: every type declared under one
    /// is a static class, which also bars it from implementing an interface (CS0714) — so it can be
    /// neither injected nor a contract.
    /// </summary>
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

    /// <summary>
    /// Proves the Helpers rule can fail. A walk over no <c>Helpers/</c> folder passes, so the
    /// predicate is run over declarations written to break it.
    /// </summary>
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

    /// <summary>
    /// The suffixes of a folder as <see cref="FeatureFiles"/> names it: the concept-qualified entry
    /// first, then the nature alone.
    /// </summary>
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

                // Features/<F>/<Word>/… — a file directly under a feature has no folder to match.
                if (parts.Length < 3)
                {
                    continue;
                }

                // Features/<F>/<Concept>/<Nature>/… — the nature is the folder one level down.
                string folder = parts.Length >= 4 && _concepts.Contains(parts[1])
                    ? $"{parts[1]}/{parts[2]}"
                    : parts[1];

                yield return (folder, Path.GetRelativePath(root, file), Path.GetFileName(file));
            }
        }
    }
}

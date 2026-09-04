using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// <c>CONTRIBUTING.md</c> says a folder under <c>Features/&lt;F&gt;/</c> is the plural of the nature
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
    /// The suffix each feature folder's files carry. A folder absent from here is one no rule
    /// checks, which <see cref="EveryFeatureFolder_IsCovered"/> refuses.
    /// </summary>
    private static readonly Dictionary<string, string[]> _suffixes = new(StringComparer.Ordinal)
    {
        ["Configurations"] = ["Configuration"],
        ["Consumers"] = ["Consumer"],
        ["Contracts"] = ["Request", "Response"],
        ["Controllers"] = ["Controller"],
        ["Directories"] = ["Directory"],
        ["Dtos"] = ["Dto"],
        ["Entities"] = [],
        ["Errors"] = ["Errors"],
        ["Events"] = ["Event"],
        ["Extensions"] = ["Extensions"],
        ["Factories"] = ["Factory"],
        ["Inspectors"] = ["Inspector"],
        ["Inventories"] = ["Inventory"],
        ["Issuers"] = ["Issuer"],
        ["Logs"] = ["Log"],
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
        ["UseCases"] = [],
        ["ValueObjects"] = [],
        ["Verifiers"] = ["Verifier"],
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
        ["ContentDecision.cs"] = "the decision StoredFileContentPolicy returns",
        ["ExternalAccountLinkDecision.cs"] = "the decision the external-account policy returns",
        ["MediaTypeSignatures.cs"] = "the magic-byte table StoredFileContentPolicy reads",

        // ASP.NET Core Identity names these, not this repository: they are what IdentityUser<Guid>
        // and IdentityRole<Guid> are subclassed as, and a …Record suffix on them would be this
        // template renaming a framework's vocabulary in its own tree.
        ["AppUser.cs"] = "ASP.NET Core Identity's own user row",
        ["AppRole.cs"] = "ASP.NET Core Identity's own role row",
        ["RefreshToken.cs"] = "the row IRefreshTokenTable reads and writes",

        // The state SigningKeyDirectory holds, not a second directory — the same shape as
        // RecordedEmails and StoredObjects, which sit where no nature word applies at all.
        ["CachedSigningKeys.cs"] = "the cache SigningKeyDirectory reads and fills",
    };

    [Fact]
    public void EveryFileUnderAFeature_CarriesItsFoldersNatureWord()
    {
        var offenders = new List<string>();
        var checkedFiles = 0;

        foreach ((string folder, string relative, string file) in FeatureFiles())
        {
            if (!_suffixes.TryGetValue(folder, out var suffixes))
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
            200,
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
            .Where(folder => !_suffixes.ContainsKey(folder))
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
                var parts = Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar);

                // Features/<F>/<Word>/… — a file directly under a feature has no folder to match.
                if (parts.Length < 3)
                {
                    continue;
                }

                yield return (parts[1], Path.GetRelativePath(root, file), Path.GetFileName(file));
            }
        }
    }
}

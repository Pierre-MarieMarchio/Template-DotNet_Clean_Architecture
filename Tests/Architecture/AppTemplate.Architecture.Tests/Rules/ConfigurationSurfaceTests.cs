using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ConfigurationSurfaceTests
{
    private static readonly Regex _sectionName = new(
        @"SectionName\s*=\s*""([A-Za-z:]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _bindableMember = new(
        @"^    public\s+[A-Za-z0-9_<>?\[\],\. ]+?\s+([A-Za-z0-9_]+)\s*\{\s*get;",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _documentedKey = new(
        @"^\|\s*`([A-Za-z0-9_]+)[:`]",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _anyHeading = new(
        @"^### ",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _sectionHeading = new(
        @"^### `([A-Za-z:]+)`",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryBoundSection_HasATableInThe_configurationGuide()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        var missing = bound.Keys
            .Where(section => !documented.ContainsKey(section))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            "A section an options class binds is one an operator has to be able to look up. " +
            $"'{_configurationGuide}' has no '### `<section>`' heading for these, so every key in " +
            "them is a setting the guide's own opening promise does not cover.");
    }

    [Fact]
    public void EveryKeyOfABoundSection_IsDocumentedUnderThatSection()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        var undocumented = new List<string>();

        foreach ((string section, SectionFacts facts) in bound)
        {
            if (!documented.TryGetValue(section, out string? text))
            {
                continue;
            }

            var rows = KeysIn(text);

            undocumented.AddRange(facts.Keys
                .Where(key => !rows.Contains(key) && !documented.ContainsKey($"{section}:{key}"))
                .Select(key => $"{section}:{key} — bound by {facts.Origin}"));
        }

        undocumented.Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"These keys are bound and validated at startup and named nowhere in '{_configurationGuide}'. " +
            "A key an operator cannot find is one they cannot set, and its default is then a decision " +
            "nobody made. Add a row to that section's table, or a '### `Section:Key`' heading of its own " +
            "where the key needs more than a row.");
    }

    [Fact]
    public void NoDocumentedKey_IsAbsentFromTheOptionsClassesBindingThatSection()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        var invented = new List<string>();

        foreach ((string section, SectionFacts facts) in bound)
        {
            if (!documented.TryGetValue(section, out string? text))
            {
                continue;
            }

            invented.AddRange(KeysIn(text)
                .Where(key => !facts.Keys.Contains(key))
                .Select(key => $"{section}:{key} — no such member on {facts.Origin}"));
        }

        invented.Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"'{_configurationGuide}' documents these keys and no options class binds them. Setting " +
            "one changes nothing, and it reads as a knob that exists — which is how a renamed key " +
            "leaves its old name behind in the guide.");
    }

    [Fact]
    public void TheScanAndTheGuide_BothProduceWhatTheyAreAssertedOver()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        bound.Count.ShouldBeGreaterThanOrEqualTo(
            18,
            "Far fewer bound sections were found than this template has, so the source scan is not " +
            "reading the tree it is meant to describe.");

        documented.Count.ShouldBeGreaterThanOrEqualTo(
            20,
            $"Far fewer sections were parsed out of '{_configurationGuide}' than it holds, so the " +
            "guide is being read with the wrong heading shape.");

        bound.Values.Sum(facts => facts.Keys.Count).ShouldBeGreaterThanOrEqualTo(
            42,
            "Far fewer bindable members were found than the options classes declare, so the member " +
            "pattern has stopped matching the shape they are written in.");

        /*#if (localAuth)*/
        bound["TwoFactor"].Keys.ShouldBe(
            ["ChallengeLifetime", "Issuer", "MaxChallengeAttempts", "RecoveryCodeCount"],
            ignoreOrder: true,
            "TwoFactor is the section this rule was written for: bound and validated with these keys, " +
            "each of which needs its row in docs/reference/configuration.md like any other.");
        /*#else
        bound["Idempotency"].Keys.ShouldBe(
            ["ClaimLease", "Enabled", "MaxKeyLength", "MaxStoredResponseBytes", "Retention"],
            ignoreOrder: true,
            "Idempotency is bound by the HTTP pipeline whatever the project was generated with, so it " +
            "is the section a scan that reads the tree at all has to find, with every key it binds.");
        #endif*/
    }

    private const string _configurationGuide = "docs/reference/configuration.md";

    private sealed record SectionFacts(IReadOnlySet<string> Keys, string Origin);

    private static SortedDictionary<string, SectionFacts> BoundSections()
    {
        var sections = new SortedDictionary<string, SectionFacts>(StringComparer.Ordinal);

        foreach (string file in SourceFiles())
        {
            string source = File.ReadAllText(file);
            var declared = _sectionName.Match(source);

            if (!declared.Success)
            {
                continue;
            }

            string section = declared.Groups[1].Value;
            string origin = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);

            var keys = _bindableMember
                .Matches(source)
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (sections.TryGetValue(section, out SectionFacts? existing))
            {
                sections[section] = new SectionFacts(
                    existing.Keys.Concat(keys).ToHashSet(StringComparer.Ordinal),
                    $"{existing.Origin} and {origin}");

                continue;
            }

            sections[section] = new SectionFacts(keys, origin);
        }

        return sections;
    }

    private static Dictionary<string, string> DocumentedSections()
    {
        string guide = File.ReadAllText(
            Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "reference", "configuration.md"));

        var headings = _anyHeading.Matches(guide);
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < headings.Count; index++)
        {
            int start = headings[index].Index;
            int end = index + 1 < headings.Count ? headings[index + 1].Index : guide.Length;
            string text = guide[start..end];

            var named = _sectionHeading.Match(text);

            if (named.Success)
            {
                sections[named.Groups[1].Value] = text;
            }
        }

        return sections;
    }

    private static HashSet<string> KeysIn(string sectionText) =>
        [.. _documentedKey.Matches(sectionText).Select(match => match.Groups[1].Value)];

    private static IEnumerable<string> SourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
}

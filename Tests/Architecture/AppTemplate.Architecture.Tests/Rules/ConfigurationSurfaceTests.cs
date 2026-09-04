using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The configuration guide against the options classes it describes, in both directions.
/// <para>
/// <c>docs/CONFIGURATION.md</c> opens by promising that "every setting the application reads is
/// listed here". Nothing but this rule holds it to that: a section with no table, or a table naming
/// a key no options class binds, is invisible to the compiler and to every other gate, and drifts
/// silently. <c>SECURITY.md</c> leans on the same promise, describing the <c>TwoFactor</c> values
/// in prose as though they were constants.
/// </para>
/// <para>
/// <b>Read from the source tree rather than from metadata</b>, for the reason
/// <c>LayoutConventionTests</c> gives: the subject is a Markdown file, and a rule about what a
/// document says has to read the document. Both hosts' options classes are scanned, and sections
/// declared by more than one class — <c>OpenTelemetry</c> is declared by the API's and the worker's —
/// contribute the union of their keys, since a key documented once is documented.
/// </para>
/// </summary>
public sealed class ConfigurationSurfaceTests
{
    /// <summary>
    /// The section a class binds to. Every options class in this template declares it the same way,
    /// which is what makes the pairing below mechanical rather than a list maintained by hand.
    /// </summary>
    private static readonly Regex _sectionName = new(
        @"SectionName\s*=\s*""([A-Za-z:]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A bindable member. <c>{ get; }</c> as well as <c>{ get; set; }</c>, because the configuration
    /// binder populates a get-only collection in place — <c>ReverseProxy:KnownProxies</c> is one, and
    /// a pattern that demanded a setter would have called it undocumentable rather than undocumented.
    /// </summary>
    private static readonly Regex _bindableMember = new(
        @"^    public\s+[A-Za-z0-9_<>?\[\],\. ]+?\s+([A-Za-z0-9_]+)\s*\{\s*get;",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A key as the guide's tables name one: the first cell of a row, in backticks. Only the leading
    /// segment is captured, because a nested collection is documented one leaf at a time —
    /// <c>ExternalIdentity</c> binds a <c>Providers</c> list and the guide has a row for
    /// <c>Providers:&lt;n&gt;:Name</c> rather than one for <c>Providers</c>, which is the more useful
    /// document and still accounts for the member.
    /// </summary>
    private static readonly Regex _documentedKey = new(
        @"^\|\s*`([A-Za-z0-9_]+)[:`]",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Every third-level heading, which is what bounds one section's text. Deliberately not only the
    /// ones naming a section: the guide ends with <c>### Not configurable</c> and
    /// <c>### Standard ASP.NET Core keys</c>, and a slice that ran to the next *named* section would
    /// hand the last named one both of those tables — which is exactly how this rule first reported
    /// <c>AllowedHosts</c> as a key of <c>ReminderWorker</c>.
    /// </summary>
    private static readonly Regex _anyHeading = new(
        @"^### ",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The section a heading names, when it names one. Only the backticked part, because several
    /// headings carry a qualifier the reader needs — <c>### `FileWorker` — `AppTemplate.Worker` only</c>.
    /// </summary>
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
                // Reported by the rule above, and reporting it twice would make one omission look
                // like two.
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

    /// <summary>
    /// The converse, and the direction that found the live defect: a row for a key that does not
    /// exist is worse than a missing row, because an operator sets it, nothing binds it, and the
    /// value they chose is silently the default.
    /// </summary>
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

    /// <summary>
    /// Proves the two extractors are live, because every assertion above is over a difference of two
    /// sets: a scan that matched nothing and a guide that parsed as nothing would agree perfectly,
    /// and all three rules would pass on a repository with no configuration at all.
    /// </summary>
    [Fact]
    public void TheScanAndTheGuide_BothProduceWhatTheyAreAssertedOver()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        bound.Count.ShouldBeGreaterThanOrEqualTo(
            25,
            "Far fewer bound sections were found than this template has, so the source scan is not " +
            "reading the tree it is meant to describe.");

        documented.Count.ShouldBeGreaterThanOrEqualTo(
            25,
            $"Far fewer sections were parsed out of '{_configurationGuide}' than it holds, so the " +
            "guide is being read with the wrong heading shape.");

        bound.Values.Sum(facts => facts.Keys.Count).ShouldBeGreaterThanOrEqualTo(
            80,
            "Far fewer bindable members were found than the options classes declare, so the member " +
            "pattern has stopped matching the shape they are written in.");

        // One section named explicitly, so that a scan which found sections but no keys — or keys
        // under the wrong section — cannot satisfy the counts above.
        bound["TwoFactor"].Keys.ShouldBe(
            ["ChallengeLifetime", "Issuer", "MaxChallengeAttempts", "RecoveryCodeCount"],
            ignoreOrder: true,
            "TwoFactor is the section this rule was written for: it was bound, validated, described " +
            "in SECURITY.md's prose, and absent from the configuration guide entirely.");
    }

    private const string _configurationGuide = "docs/CONFIGURATION.md";

    /// <summary>What a section binds, and where to look when a key of it is unaccounted for.</summary>
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

            // The union, not the last one seen: OpenTelemetry is bound by the API's options class and
            // the worker's, and the two do not carry the same keys.
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

    /// <summary>
    /// Each heading in the guide against the text under it, up to the next heading of the same level.
    /// </summary>
    private static Dictionary<string, string> DocumentedSections()
    {
        string guide = File.ReadAllText(
            Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "CONFIGURATION.md"));

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

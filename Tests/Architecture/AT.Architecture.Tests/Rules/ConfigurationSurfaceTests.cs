using System.Text.Json;
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
        @"^### `([A-Za-z0-9:]+)`",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _directRead = new(
        @"GetConnectionString\((?<connection>[^()]+)\)"
        + @"|GetSection\((?<section>[^()]+)\)(?:\s*\.GetValue<[^<>]+>\((?<member>[^()]+(?:\([^()]*\))?)\))?"
        + @"|[cC]onfiguration\s*(?:\[(?<indexed>[^\[\]]+)\]|\.GetValue<[^<>]+>\((?<valued>""[^""]*""))",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _stringConstant = new(
        @"const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _typeName = new(
        @"\b(?:class|record|struct)\s+(?<name>\w+)",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _interpolation = new(
        @"\{(?<expression>[^{}]+)\}",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _environmentKey = new(
        @"^\s*(?<key>[A-Za-z][A-Za-z0-9]*(?:__[A-Za-z0-9]+)+)\s*[:=]",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryKeyOfAShippedAppsettings_IsDocumentedUnderItsSection()
    {
        var keys = ShippedAppsettingsKeys();

        keys.ShouldNotBeEmpty("No key was read from the shipped appsettings files, so the scan reads nothing.");

        UndocumentedKeys(keys, BoundSections().Keys, DocumentedSections()).ShouldBeEmpty(
            $"These keys are shipped in an appsettings file under a section '{_configurationGuide}' covers, " +
            "and have no row there. A key an operator finds in the file and cannot look up is a default " +
            "nobody explained: add its row to that section's table, or drop it from the file.");
    }

    [Fact]
    public void EveryKeyTheComposeFilesAndEnvExampleSet_IsDocumentedUnderItsSection()
    {
        var keys = EnvironmentKeys();

        keys.ShouldNotBeEmpty("No Section__Key variable was read from the Compose files, so the scan reads nothing.");

        UndocumentedKeys(keys, BoundSections().Keys, DocumentedSections()).ShouldBeEmpty(
            "These environment variables set a key (`Section__Key` is `Section:Key`) of a section " +
            $"'{_configurationGuide}' covers, and have no row there. A deployment copies what the Compose " +
            "files and .env.example show: add the key's row to that section's table, or stop setting it.");
    }

    [Fact]
    public void EveryKeyTheCodeReadsDirectly_IsDocumented()
    {
        var sources = SourceFiles().ToDictionary(
            file => Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/'),
            File.ReadAllText,
            StringComparer.Ordinal);
        var reads = DirectReads(sources);

        reads.ShouldContain(read => read.Key == "ConnectionStrings:Default", "the connection string read was not found, so the scan of direct reads reads nothing.");

        UndocumentedReads(reads, DocumentedSections()).ShouldBeEmpty(
            "The code reads these keys straight from the configuration, outside an options class, and " +
            $"'{_configurationGuide}' names them nowhere. An operator cannot set a key the guide does not " +
            "name: add its row under its section, or a '### `Section:Key`' heading of its own.");
    }

    [Fact]
    public void TheDirectReadScan_IsSensitive_AndResolvesLiteralsConstantsAndInterpolations()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Widget.cs"] = """
                public sealed class WidgetOptions
                {
                    public const string SectionName = "Widget";
                }

                public static class Connection
                {
                    public const string Name = "Default";

                    public static void Read(IConfiguration configuration)
                    {
                        _ = configuration.GetConnectionString(Name);
                        _ = configuration[$"{WidgetOptions.SectionName}:Mode"];
                        _ = configuration["Gadget:Size"];
                        _ = configuration.GetValue<int>("Gadget:Weight");
                        _ = configuration.GetSection(WidgetOptions.SectionName).GetValue<bool>(nameof(WidgetOptions.Enabled));
                        _ = configuration.GetSection("Gizmo");
                    }
                }
                """,
        };

        var reads = DirectReads(sources);

        reads.Select(read => read.Key).ShouldBe(
            ["ConnectionStrings:Default", "Widget:Mode", "Gadget:Size", "Gadget:Weight", "Widget:Enabled", "Gizmo"],
            ignoreOrder: true);

        var documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings"] = "### `ConnectionStrings`\n| `Default` | string |\n",
            ["Widget"] = "### `Widget`\n| `Enabled` | bool |\n",
            ["Widget:Mode"] = "### `Widget:Mode`\n",
            ["Gadget"] = "### `Gadget`\n| `Size` | int |\n",
        };

        UndocumentedReads(reads, documented).ShouldBe(["Gadget:Weight (Widget.cs)", "Gizmo (Widget.cs)"]);
    }

    [Fact]
    public void TheKeyCheck_IsSensitive()
    {
        var documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Gadget"] = "### `Gadget`\n\n| Key | Type |\n|---|---|\n| `Size` | int |\n| `Colours:<n>:Name` | string |\n",
            ["Gadget:Nested"] = "### `Gadget:Nested`\n\n| Key | Type |\n|---|---|\n| `Depth` | int |\n",
        };

        string[] bound = ["Gadget"];

        UndocumentedKeys(
            [
                ("Gadget:Size", "a.json"),
                ("Gadget:Colours:0:Name", "a.json"),
                ("Gadget:Nested:Depth", "a.json"),
                ("Gadget:Weight", "a.json"),
                ("Gadget:Nested:Width", "b.yml"),
                ("Logging:LogLevel:Default", "a.json"),
            ],
            bound,
            documented).ShouldBe(
            [
                "Gadget:Nested:Width (b.yml)",
                "Gadget:Weight (a.json)",
            ]);

        _environmentKey.Matches("  Gadget__Size: ${X:-1}\n  # Gadget__Skipped: 1\nPLAIN_VARIABLE=1\nGadget__Nested__Depth=2\n")
            .Select(match => match.Groups["key"].Value)
            .ShouldBe(["Gadget__Size", "Gadget__Nested__Depth"]);
    }

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
            "them is a setting the guide's own opening promise does not cover. Add a '### `<section>`' " +
            "heading with a table of its keys to that page, as 'Adding a setting' describes.");
    }

    [Fact]
    public void EveryKeyOfABoundSection_IsDocumentedUnderThatSection()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        var undocumented = Undocumented(bound, documented);

        undocumented.ShouldBeEmpty(
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

        var invented = Invented(bound, documented);

        invented.ShouldBeEmpty(
            $"'{_configurationGuide}' documents these keys and no options class binds them. Setting " +
            "one changes nothing, and it reads as a knob that exists — which is how a renamed key " +
            "leaves its old name behind in the guide. Delete the row of each key named here, or rename " +
            "it to the member the options class declares.");
    }

    [Fact]
    public void TheScanAndTheGuide_BothProduceWhatTheyAreAssertedOver()
    {
        var bound = BoundSections();
        var documented = DocumentedSections();

        bound.Count.ShouldBeGreaterThanOrEqualTo(
            /*#if (api)*/
            18,
            /*#else
            8,
            #endif*/
            "Far fewer bound sections were found than this template has, so the source scan is not " +
            "reading the tree it is meant to describe.");

        documented.Count.ShouldBeGreaterThanOrEqualTo(
            20,
            $"Far fewer sections were parsed out of '{_configurationGuide}' than it holds, so the " +
            "guide is being read with the wrong heading shape.");

        bound.Values.Sum(facts => facts.Keys.Count).ShouldBeGreaterThanOrEqualTo(
            /*#if (api)*/
            42,
            /*#else
            20,
            #endif*/
            "Far fewer bindable members were found than the options classes declare, so the member " +
            "pattern has stopped matching the shape they are written in.");

        /*#if (localAuth)*/
        bound["TwoFactor"].Keys.ShouldBe(
            ["ChallengeLifetime", "Issuer", "MaxChallengeAttempts", "RecoveryCodeCount"],
            ignoreOrder: true,
            "TwoFactor is the section this rule was written for: bound and validated with these keys, " +
            "each of which needs its row in docs/reference/configuration.md like any other.");
        /*#elseif (api)
        bound["Idempotency"].Keys.ShouldBe(
            ["ClaimLease", "Enabled", "MaxKeyLength", "MaxStoredResponseBytes", "Retention"],
            ignoreOrder: true,
            "Idempotency is bound by the HTTP pipeline whatever the project was generated with, so it " +
            "is the section a scan that reads the tree at all has to find, with every key it binds.");
        #else
        bound["Database"].Keys.ShouldBe(
            ["CommandTimeoutSeconds", "MaxPoolSize"],
            ignoreOrder: true,
            "Database is bound by the persistence module whatever the project was generated with, so it " +
            "is the section a scan that reads the tree at all has to find, with every key it binds.");
        #endif*/
    }

    [Fact]
    public void TheScan_IsSensitive_AndReadsASectionItsKeysAndTheGuidesHeadings()
    {
        const string options = """
            public sealed class WidgetOptions
            {
                public const string SectionName = "Widget:Limits";

                public int MaxCount { get; set; }

                public TimeSpan Lifetime { get; init; }

                private int Hidden { get; set; }
            }
            """;

        _sectionName.Match(options).Groups[1].Value.ShouldBe("Widget:Limits");
        _bindableMember.Matches(options).Select(match => match.Groups[1].Value).ShouldBe(["MaxCount", "Lifetime"]);

        const string guide = """
            ## Widget

            ### `Widget:Limits`

            | Key | Default |
            |---|---|
            | `MaxCount` | 10 |
            | `Ghost` | 1 |

            ### Prose heading
            """;

        var documented = SectionsOf(guide);

        documented.Keys.ShouldBe(["Widget:Limits"]);
        KeysIn(documented["Widget:Limits"]).ShouldBe(["MaxCount", "Ghost"], ignoreOrder: true);
    }

    [Fact]
    public void TheComparison_IsSensitive_AndNamesAnUndocumentedKeyAndAnInventedOne()
    {
        var bound = new SortedDictionary<string, SectionFacts>(StringComparer.Ordinal)
        {
            ["Widget"] = new(new HashSet<string>(["MaxCount", "Lifetime"], StringComparer.Ordinal), "Widget.cs"),
            ["Gadget"] = new(new HashSet<string>(["Size"], StringComparer.Ordinal), "Gadget.cs"),
        };
        var documented = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Widget"] = "### `Widget`\n| `MaxCount` | 1 |\n| `Ghost` | 2 |\n",
        };

        Undocumented(bound, documented).ShouldBe(["Widget:Lifetime \u2014 bound by Widget.cs"]);
        Invented(bound, documented).ShouldBe(["Widget:Ghost \u2014 no such member on Widget.cs"]);

        documented["Widget:Lifetime"] = "### `Widget:Lifetime`\n";
        Undocumented(bound, documented).ShouldBeEmpty();
    }

    private const string _configurationGuide = "docs/reference/configuration.md";

    private static readonly string[] _sourceRoots =
    [
        "Src",
        /*#if (templateMaintenance)*/
        ".template.config/starters",
        /*#endif*/
    ];

    private sealed record SectionFacts(IReadOnlySet<string> Keys, string Origin);

    private static List<string> UndocumentedKeys(
        IEnumerable<(string Key, string Origin)> keys,
        IEnumerable<string> bound,
        IReadOnlyDictionary<string, string> documented)
    {
        string[] boundSections = [.. bound];
        string[] covered = [.. boundSections.Concat(documented.Keys).Distinct(StringComparer.Ordinal)];
        var undocumented = new SortedSet<string>(StringComparer.Ordinal);

        foreach ((string key, string origin) in keys)
        {
            var sections = covered.Where(section => key.StartsWith(section + ":", StringComparison.Ordinal)).ToList();

            if (!sections.Intersect(boundSections, StringComparer.Ordinal).Any())
            {
                continue;
            }

            string section = sections.OrderByDescending(candidate => candidate.Length).First();
            string first = key[(section.Length + 1)..].Split(':')[0];

            bool documentedUnderIt = (documented.TryGetValue(section, out string? text) && KeysIn(text).Contains(first))
                || documented.ContainsKey($"{section}:{first}");

            if (!documentedUnderIt)
            {
                undocumented.Add($"{key} ({origin})");
            }
        }

        return [.. undocumented];
    }

    private static List<(string Key, string Origin)> ShippedAppsettingsKeys()
    {
        var keys = new List<(string Key, string Origin)>();
        string presentation = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src", "Presentation");
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        foreach (string file in Directory.EnumerateFiles(presentation, "appsettings*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), options);
            string origin = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/');

            Flatten(document.RootElement, string.Empty, origin, keys);
        }

        return keys;
    }

    private static void Flatten(JsonElement element, string path, string origin, List<(string Key, string Origin)> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, path.Length == 0 ? property.Name : $"{path}:{property.Name}", origin, keys);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{path}:{index++}", origin, keys);
                }

                break;
            default:
                keys.Add((path, origin));
                break;
        }
    }

    private static List<(string Key, string Origin)> EnvironmentKeys()
    {
        var keys = new List<(string Key, string Origin)>();
        string root = ProjectReferenceGraph.RepositoryRoot;

        string[] files =
        [
            .. Directory.EnumerateFiles(root, "docker-compose*.yml"),
            Path.Combine(root, ".env.example"),
        ];

        foreach (string file in files.Where(File.Exists))
        {
            string origin = Path.GetFileName(file);

            keys.AddRange(_environmentKey
                .Matches(File.ReadAllText(file))
                .Select(match => (match.Groups["key"].Value.Replace("__", ":", StringComparison.Ordinal), origin)));
        }

        return keys;
    }

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

    private static List<(string Key, string Origin)> DirectReads(IReadOnlyDictionary<string, string> sources)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string source in sources.Values)
        {
            foreach (Match constant in _stringConstant.Matches(source))
            {
                string? owner = _typeName.Matches(source[..constant.Index]).LastOrDefault()?.Groups["name"].Value;

                if (owner is not null)
                {
                    constants.TryAdd($"{owner}.{constant.Groups["name"].Value}", constant.Groups["value"].Value);
                }
            }
        }

        var reads = new List<(string Key, string Origin)>();

        foreach ((string origin, string source) in sources)
        {
            var local = _stringConstant.Matches(source)
                .GroupBy(constant => constant.Groups["name"].Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value, StringComparer.Ordinal);

            string? Resolve(string expression)
            {
                string text = expression.Trim();

                if (text.StartsWith("$\"", StringComparison.Ordinal) && text.EndsWith('"'))
                {
                    var parts = _interpolation.Matches(text[2..^1]).Select(part => Resolve(part.Groups["expression"].Value)).ToList();

                    return parts.Contains(null) ? null : _interpolation.Replace(text[2..^1], part => Resolve(part.Groups["expression"].Value)!);
                }

                if (text.StartsWith('"') && text.EndsWith('"') && text.Length >= 2)
                {
                    return text[1..^1];
                }

                if (text.StartsWith("nameof(", StringComparison.Ordinal) && text.EndsWith(')'))
                {
                    return text[7..^1].Split('.')[^1];
                }

                return constants.TryGetValue(text, out string? qualified) ? qualified
                    : local.TryGetValue(text, out string? unqualified) ? unqualified
                    : null;
            }

            foreach (Match read in _directRead.Matches(source))
            {
                string? key = read.Groups["connection"].Success
                    ? Resolve(read.Groups["connection"].Value) is { } name ? $"ConnectionStrings:{name}" : null
                    : read.Groups["section"].Success
                        ? Resolve(read.Groups["section"].Value) is { } section
                            ? read.Groups["member"].Success
                                ? Resolve(read.Groups["member"].Value) is { } member ? $"{section}:{member}" : null
                                : section
                            : null
                        : Resolve(read.Groups[read.Groups["indexed"].Success ? "indexed" : "valued"].Value);

                if (key is not null)
                {
                    reads.Add((key, origin));
                }
            }
        }

        return reads;
    }

    private static List<string> UndocumentedReads(
        IEnumerable<(string Key, string Origin)> reads,
        IReadOnlyDictionary<string, string> documented) =>
        [.. reads
            .Where(read => !IsDocumented(read.Key, documented))
            .Select(read => $"{read.Key} ({read.Origin})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    private static bool IsDocumented(string key, IReadOnlyDictionary<string, string> documented)
    {
        if (documented.ContainsKey(key))
        {
            return true;
        }

        string[] segments = key.Split(':');

        return Enumerable.Range(1, segments.Length - 1).Any(length =>
            documented.TryGetValue(string.Join(':', segments[..length]), out string? text)
            && KeysIn(text).Contains(segments[length]));
    }

    private static Dictionary<string, string> DocumentedSections() =>
        SectionsOf(File.ReadAllText(
            Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "reference", "configuration.md")));

    private static List<string> Undocumented(
        SortedDictionary<string, SectionFacts> bound,
        Dictionary<string, string> documented)
    {
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

        return [.. undocumented.Order(StringComparer.Ordinal)];
    }

    private static List<string> Invented(
        SortedDictionary<string, SectionFacts> bound,
        Dictionary<string, string> documented)
    {
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

        return [.. invented.Order(StringComparer.Ordinal)];
    }

    private static Dictionary<string, string> SectionsOf(string guide)
    {
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
                string section = named.Groups[1].Value;
                sections[section] = sections.TryGetValue(section, out string? before) ? before + text : text;
            }
        }

        return sections;
    }

    private static HashSet<string> KeysIn(string sectionText) =>
        [.. _documentedKey.Matches(sectionText).Select(match => match.Groups[1].Value)];

    private static IEnumerable<string> SourceFiles() =>
        _sourceRoots
            .Select(root => Path.Combine(ProjectReferenceGraph.RepositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Where(file => !file.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
}

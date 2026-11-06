using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddSetting;

internal static class NewSetting
{
    internal const string Usage =
        "Usage: dotnet cleanarch add setting <Features> <Section> --keys \"Name:type[(min..max)][=default], ...\"\n"
        + "           [--dry-run]\n"
        + "  <Features>  the folder of an existing feature, composed by at least one host, e.g. Invoices\n"
        + "  <Section>   the configuration section, PascalCase, e.g. InvoiceReminders; its options class\n"
        + "              is <Section>Options and the section is the type's name without Options\n"
        + "  --keys      required. One entry per key, PascalCase name, then a type from this closed set:\n"
        + "              int(min..max)=default       a number, both bounds and a default inside them\n"
        + "              long(min..max)=default      the same, 64 bits\n"
        + "              duration(min..max)=default  a TimeSpan of whole seconds, written 00:00:30 or 1.00:00:00\n"
        + "              bool=default                true or false; it has no invalid value to refuse\n"
        + "              string(min..max)[=default]  a length range; without a default the key is required,\n"
        + "                                          so its minimum is 1 and an absent value stops the host\n"
        + "              secret                      a string that must be present and stays empty in\n"
        + "                                          appsettings.json: it comes from the environment\n"
        + "              A string default holds letters, digits, spaces and . _ : / @ - only.\n"
        + "              At least one key is validated, so a section is never all bool.\n"
        + "  --dry-run   print what would be written, and write nothing.";

    private static readonly string[] _reservedSections =
    [
        "AllowedHosts", "ConfigurationGuide", "ConnectionStrings", "Kestrel", "Logging", "Serilog",
    ];

    private static readonly string[] _reservedMembers = ["SectionName"];

    private static readonly Regex _keySpec = new(
        @"^(?<name>[^:]*):(?<type>[a-z]+)(?:\((?<bounds>[^()]*)\))?(?:=(?<default>.*))?$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _safeText = new(@"^(?:[A-Za-z0-9._:/@-](?:[A-Za-z0-9 ._:/@-]*[A-Za-z0-9._:/@-])?)?$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _sectionName = new(@"SectionName\s*=\s*""([A-Za-z:]+)""", RegexOptions.None, TimeSpan.FromSeconds(2));

    internal static int Run(AddSettingRequest request, GenerationContext context)
    {
        var problems = new List<string>();
        var spec = Check(request, context.Root, context.Product, problems);

        return spec is null
            ? Refusal.Write(context.Error, problems, Usage)
            : new SettingGeneration(spec, context).Generate();
    }

    private static SettingSpec? Check(AddSettingRequest request, string root, string product, List<string> problems)
    {
        string features = request.Features;
        string section = request.Section;

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!CSharpNames.PascalCase.IsMatch(section))
        {
            problems.Add($"'{section}' is not a section name: <Section> is PascalCase, e.g. InvoiceReminders.");
        }
        else
        {
            problems.AddRange(SectionProblems(section));
        }

        var keys = ReadKeys(request.Keys, problems);

        if (problems.Count == 0 && keys.All(key => !key.IsValidated))
        {
            problems.Add("--keys holds only bool keys, so the section validates nothing and the host would start with any value. Add a bounded key, or leave the setting to a constant.");
        }

        if (problems.Count > 0)
        {
            return null;
        }

        string prefix = product;

        string application = Full(root, SourceProject(prefix, "Application"));

        if (!Directory.Exists(Path.Combine(application, "Features", features)))
        {
            string known = Directory.Exists(Path.Combine(application, "Features"))
                ? string.Join(", ", Directory.EnumerateDirectories(Path.Combine(application, "Features")).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
                : string.Empty;

            problems.Add(
                $"No feature called {features}: '{SourceProject(prefix, "Application")}/Features/{features}' does not exist. "
                + (known.Length == 0 ? "This tree has no feature yet; write one first, as docs/guides/adding-a-feature.md says." : $"The features here are {known}."));

            return null;
        }

        problems.AddRange(TakenSections(root, section));

        if (problems.Count > 0)
        {
            return null;
        }

        var manifest = FeatureManifest.Read(root, features, out string? unread);

        if (unread is not null)
        {
            problems.Add($"'{FeatureManifest.PathOf(features)}' {unread}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        string module = Path.Combine(application, "ApplicationModule.cs");
        bool takes = File.Exists(module) && TakesConfiguration(File.ReadAllText(module), features);

        return new SettingSpec(prefix, features, section, keys, manifest?.Path, takes);
    }

    private static List<string> SectionProblems(string section)
    {
        var problems = new List<string>();

        if (section.EndsWith("Options", StringComparison.Ordinal))
        {
            problems.Add($"'{section}' ends in 'Options', which the class name appends itself: the section is the type's name without it. Name the section {section[..^"Options".Length]}.");
        }

        if (CSharpNames.Keywords.Contains(CSharpNames.Lower(section), StringComparer.Ordinal))
        {
            problems.Add($"'{section}' becomes the C# keyword '{CSharpNames.Lower(section)}' once its first letter is lowered. Choose another name.");
        }

        if (_reservedSections.Contains(section, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"'{section}' is a section ASP.NET Core or this project's appsettings.json already reads. Choose another name.");
        }

        if (section.Length > 60)
        {
            problems.Add("A section name longer than 60 characters makes a path that some tools cannot open. Shorten it.");
        }

        return problems;
    }

    private static List<string> TakenSections(string root, string section)
    {
        var taken = new List<string>();

        bool Clashes(string existing) =>
            string.Equals(existing, section, StringComparison.OrdinalIgnoreCase)
            || existing.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)
            || section.StartsWith(existing + ":", StringComparison.OrdinalIgnoreCase);

        string src = Path.Combine(root, Src);

        foreach (string file in SourceFiles(src, "*.cs"))
        {
            taken.AddRange(_sectionName.Matches(File.ReadAllText(file))
                .Select(match => match.Groups[1].Value)
                .Where(Clashes)
                .Select(bound => $"The section {section} is already bound by {Path.GetRelativePath(root, file).Replace('\\', '/')} ({bound}). A section is written once; after that it is edited."));
        }

        string presentation = Full(root, Presentation);

        taken.AddRange(SourceFiles(presentation, "appsettings*.json")
            .Where(file => TopLevelNames(File.ReadAllText(file)).Any(name => string.Equals(name, section, StringComparison.OrdinalIgnoreCase)))
            .Select(file => $"{Path.GetRelativePath(root, file).Replace('\\', '/')} already holds a section called {section}; configuration keys are not case-sensitive."));

        string guide = Path.Combine(root, "docs", "reference", "configuration.md");

        if (File.Exists(guide) && File.ReadAllText(guide).Contains($"### `{section}`", StringComparison.Ordinal))
        {
            taken.Add($"docs/reference/configuration.md already has a heading for {section}.");
        }

        return [.. taken.Distinct(StringComparer.Ordinal)];
    }

    internal static IEnumerable<string> TopLevelNames(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            return document.RootElement.ValueKind == JsonValueKind.Object ? [.. document.RootElement.EnumerateObject().Select(property => property.Name)] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<SettingKey> ReadKeys(string text, List<string> problems)
    {
        var keys = new List<SettingKey>();
        string[] entries = [.. text.Split(',').Select(entry => entry.Trim())];

        foreach (string entry in entries)
        {
            if (entry.Length == 0)
            {
                problems.Add("--keys holds an empty entry; separate keys with one comma each.");

                continue;
            }

            var match = _keySpec.Match(entry);

            if (!match.Success)
            {
                problems.Add($"'{entry}' in --keys is not written Name:type[(min..max)][=default].");

                continue;
            }

            string name = match.Groups["name"].Value;

            if (!CSharpNames.PascalCase.IsMatch(name))
            {
                problems.Add($"'{name}' in --keys is not a key name: PascalCase, e.g. MaxItems.");

                continue;
            }

            if (CSharpNames.Keywords.Contains(CSharpNames.Lower(name), StringComparer.Ordinal) || _reservedMembers.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' in --keys would be a C# keyword or a member the options class already declares. Choose another name.");

                continue;
            }

            if (keys.Any(key => string.Equals(key.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"--keys names {name} more than once; configuration keys are not case-sensitive.");

                continue;
            }

            if (ReadKey(name, match.Groups["type"].Value, match.Groups["bounds"], match.Groups["default"], entry, problems) is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private static SettingKey? ReadKey(string name, string type, Group bounds, Group defaultGroup, string entry, List<string> problems)
    {
        string? text = defaultGroup.Success ? defaultGroup.Value : null;

        switch (type)
        {
            case "bool":
                if (bounds.Success)
                {
                    problems.Add($"'{entry}': a bool has no bounds.");

                    return null;
                }

                if (text is not ("true" or "false"))
                {
                    problems.Add($"'{entry}': a bool needs its default, =true or =false, since an absent value cannot be told from false.");

                    return null;
                }

                return new SettingKey(name, KeyKind.Bool, 0, 0, text);

            case "secret":
                if (bounds.Success || text is not null)
                {
                    problems.Add($"'{entry}': a secret has neither bounds nor a default, since a value in a tracked file is no secret.");

                    return null;
                }

                return new SettingKey(name, KeyKind.Secret, 1, 0, null);

            case "string":
                return ReadText(name, bounds, text, entry, problems);

            case "int" or "long" or "duration":
                return ReadNumber(name, type, bounds, text, entry, problems);

            default:
                problems.Add($"'{entry}': '{type}' is not a type of this tool. The types are int, long, duration, bool, string and secret.");

                return null;
        }
    }

    private static SettingKey? ReadText(string name, Group bounds, string? text, string entry, List<string> problems)
    {
        if (!bounds.Success || !TryBounds(bounds.Value, out string low, out string high) || !long.TryParse(low, NumberStyles.None, CultureInfo.InvariantCulture, out long min)
            || !long.TryParse(high, NumberStyles.None, CultureInfo.InvariantCulture, out long max))
        {
            problems.Add($"'{entry}': a string needs its length range, string(min..max), two whole numbers.");

            return null;
        }

        if (max is < 1 or > 4096 || min > max)
        {
            problems.Add($"'{entry}': a string's length range runs from min to max, max between 1 and 4096.");

            return null;
        }

        if (text is null && min < 1)
        {
            problems.Add($"'{entry}': a string with no default is required, and an absent value is empty, so its minimum length must be 1 or more. Give it a default, or raise the minimum.");

            return null;
        }

        if (text is not null && (!_safeText.IsMatch(text) || text.Length < min || text.Length > max))
        {
            problems.Add($"'{entry}': a string default holds letters, digits, spaces and . _ : / @ - only, never at an end of it, and fits the length range.");

            return null;
        }

        return new SettingKey(name, KeyKind.Text, min, max, text);
    }

    private static SettingKey? ReadNumber(string name, string type, Group bounds, string? text, string entry, List<string> problems)
    {
        var kind = type switch { "int" => KeyKind.Int, "long" => KeyKind.Long, _ => KeyKind.Duration };

        if (!bounds.Success || !TryBounds(bounds.Value, out string low, out string high) || !TryNumber(kind, low, out long min) || !TryNumber(kind, high, out long max))
        {
            problems.Add(
                $"'{entry}': {type} needs its bounds, {type}(min..max), "
                + (kind == KeyKind.Duration ? "two whole-second durations such as 00:00:01..01:00:00." : "two whole numbers, neither the extreme of the type.")
                + string.Empty);

            return null;
        }

        if (min >= max)
        {
            problems.Add($"'{entry}': min must be below max.");

            return null;
        }

        if (text is null)
        {
            problems.Add($"'{entry}': a {type} needs its default, =value inside its bounds: an absent number cannot be told from 0, so the file and the class both carry the value a deployment gets.");

            return null;
        }

        if (!TryNumber(kind, text, out long value) || value < min || value > max)
        {
            problems.Add($"'{entry}': the default {text} is not {(kind == KeyKind.Duration ? "a whole-second duration" : "a whole number")} inside {low}..{high}.");

            return null;
        }

        return new SettingKey(name, kind, min, max, value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryBounds(string text, out string low, out string high)
    {
        int split = text.IndexOf("..", StringComparison.Ordinal);

        (low, high) = split > 0 ? (text[..split].Trim(), text[(split + 2)..].Trim()) : (string.Empty, string.Empty);

        return low.Length > 0 && high.Length > 0;
    }

    private static bool TryNumber(KeyKind kind, string text, out long value)
    {
        value = 0;

        if (kind == KeyKind.Duration)
        {
            if (!text.Contains(':', StringComparison.Ordinal)
                || !TimeSpan.TryParseExact(text, "c", CultureInfo.InvariantCulture, out var span)
                || span.Ticks % TimeSpan.TicksPerSecond != 0
                || span < TimeSpan.FromSeconds(1)
                || span > TimeSpan.FromDays(3650))
            {
                return false;
            }

            value = (long)span.TotalSeconds;

            return true;
        }

        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        long extreme = kind == KeyKind.Int ? int.MaxValue : long.MaxValue;
        long floor = kind == KeyKind.Int ? int.MinValue : long.MinValue;

        return value > floor && value < extreme;
    }

    private static bool TakesConfiguration(string moduleText, string features)
    {
        var signature = new Regex($@"public static IServiceCollection Add{features}\(this IServiceCollection services(?<rest>[^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(moduleText);

        return signature.Success && Regex.IsMatch(signature.Groups["rest"].Value, @"IConfiguration configuration\b", RegexOptions.None, TimeSpan.FromSeconds(1));
    }
}

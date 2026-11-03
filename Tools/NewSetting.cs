#:property UseAppHost=false

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewSetting
{
    private const string _manifestFolder = ".feature-manifest";

    private const string _conditionOpen = "#" + "if (";

    private const string _conditionClose = "#" + "endif";

    private const string _usage =
        "Usage: dotnet run Tools/NewSetting.cs <Features> <Section> --keys \"Name:type[(min..max)][=default], ...\"\n"
        + "           [--dry-run] [--root <path>]\n"
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
        + "  --dry-run   print what would be written, and write nothing.\n"
        + "  --root      the tree to write into, default the current directory.\n"
        + "       dotnet run Tools/NewSetting.cs --self-test";

    private static readonly string[] _optionNames = ["--keys", "--root", "--dry-run"];

    private static readonly string[] _reservedSections =
    [
        "AllowedHosts", "ConfigurationGuide", "ConnectionStrings", "Kestrel", "Logging", "Serilog",
    ];

    private static readonly string[] _reservedMembers = ["SectionName"];

    private static readonly string[] _keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    private static readonly Regex _pascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _keySpec = new(
        @"^(?<name>[^:]*):(?<type>[a-z]+)(?:\((?<bounds>[^()]*)\))?(?:=(?<default>.*))?$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _safeText = new(@"^(?:[A-Za-z0-9._:/@-](?:[A-Za-z0-9 ._:/@-]*[A-Za-z0-9._:/@-])?)?$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _sectionName = new(@"SectionName\s*=\s*""([A-Za-z:]+)""", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _usingLine = new(@"^using [A-Z][\w.]*;$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private enum Kind
    {
        Int,
        Long,
        Duration,
        Bool,
        Text,
        Secret,
    }

    private sealed record Emission(string Path, string Content);

    private sealed record Edit(string Path, string Description, Func<string, string?> Apply);

    private sealed record Request(string? Features, string? Section, string? Keys, string Root, bool DryRun);

    private sealed record Key(string Name, Kind Kind, long Min, long Max, string? Default)
    {
        internal bool IsNumeric => Kind is Kind.Int or Kind.Long or Kind.Duration;

        internal bool IsValidated => Kind != Kind.Bool;

        internal bool IsRequired => Kind == Kind.Secret || (Kind == Kind.Text && Default is null);

        internal string Type => Kind switch
        {
            Kind.Int => "int",
            Kind.Long => "long",
            Kind.Duration => "timespan",
            Kind.Bool => "bool",
            _ => "string",
        };

        internal string ClrType => Kind switch
        {
            Kind.Int => "int",
            Kind.Long => "long",
            Kind.Duration => "TimeSpan",
            Kind.Bool => "bool",
            _ => "string",
        };

        internal string Bound(long value) => Kind == Kind.Duration ? Span(value).ToString("c", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

        internal string Literal(long value) => Kind switch
        {
            Kind.Duration => SpanExpression(value),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        internal string DefaultLiteral => Kind switch
        {
            Kind.Text => $"\"{Default}\"",
            Kind.Secret => "string.Empty",
            Kind.Bool => Default!,
            _ => Literal(long.Parse(Default!, CultureInfo.InvariantCulture)),
        };

        internal string JsonValue => Kind switch
        {
            Kind.Secret => "\"\"",
            Kind.Text => $"\"{Default}\"",
            Kind.Duration => "\"" + Span(long.Parse(Default!, CultureInfo.InvariantCulture)).ToString("c", CultureInfo.InvariantCulture) + "\"",
            _ => Default!,
        };

        internal string DocumentedDefault => Kind switch
        {
            Kind.Secret => "`\"\"`",
            Kind.Text => Default is null ? "—" : $"`{Default}`",
            Kind.Duration => $"`{Span(long.Parse(Default!, CultureInfo.InvariantCulture)).ToString("c", CultureInfo.InvariantCulture)}`",
            _ => $"`{Default}`",
        };
    }

    private static string UpperSnake(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? "_" + character : char.ToUpperInvariant(character).ToString()));

    private sealed record Spec(string Prefix, string Features, string Section, IReadOnlyList<Key> Keys, string? Manifest, bool TakesConfiguration)
    {
        internal string Options => Section + "Options";

        internal string Validator => Section + "OptionsValidator";

        internal string Namespace => $"{Prefix}.Application.Features.{Features}.Configuration.Options";

        internal string Variable(Key key) => $"{UpperSnake(Section)}_{UpperSnake(key.Name)}";
    }

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(_usage);

            return 0;
        }

        return Run(args, Console.Out, Console.Error);
    }

    private static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);
        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        if (spec is null)
        {
            return Refuse(error, problems);
        }

        var emissions = Emissions(spec, root);
        string[] existing = [.. emissions.Select(emission => emission.Path).Where(path => File.Exists(Path.Combine(root, path)))];

        if (existing.Length > 0)
        {
            problems.Add($"{existing.Length} of the files to write already exist, and none is ever overwritten:\n" + string.Join("\n", existing.Select(path => $"  {path}")));

            return Refuse(error, problems);
        }

        var (edits, hosts, documents) = Edits(spec, root, problems);

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        var changed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var edit in edits)
        {
            string current = changed.TryGetValue(edit.Path, out string? pending) ? pending : documents[edit.Path];
            string? next = edit.Apply(current);

            if (next is null)
            {
                problems.Add($"{edit.Description}: its anchor is not in {edit.Path}, so nothing could be written. Fix that file, or make this edit by hand and leave the setting to the steps of docs/guides/adding-a-setting.md.");

                continue;
            }

            changed[edit.Path] = next;
        }

        foreach (string path in changed.Where(pair => pair.Value == documents[pair.Key]).Select(pair => pair.Key).ToList())
        {
            changed.Remove(path);
        }

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        Emission? manifest = spec.Manifest is null ? null : ManifestWith(root, spec.Manifest, emissions);

        if (!request.DryRun)
        {
            try
            {
                WriteAll(root, emissions, changed, manifest);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

                return 1;
            }
        }

        Report(output, spec, emissions, changed.Keys, hosts, request.DryRun);

        return 0;
    }

    private static int Refuse(TextWriter error, List<string> problems)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(_usage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return 1;
    }

    private static Request Parse(string[] args, List<string> problems)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dryRun = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (!argument.StartsWith('-'))
            {
                positional.Add(argument);

                continue;
            }

            string name = argument;
            string? value = null;
            int equals = argument.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }

            if (!_optionNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' is not an option. The options are {string.Join(", ", _optionNames)}.");

                continue;
            }

            if (name == "--dry-run")
            {
                dryRun = true;

                continue;
            }

            if (value is null)
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    problems.Add($"'{name}' needs a value.");

                    continue;
                }

                value = args[++index];
            }

            if (!options.TryAdd(name, value))
            {
                problems.Add($"'{name}' is given twice; it takes one value.");
            }
        }

        if (positional.Count != 2)
        {
            problems.Add($"Expected two names, <Features> and <Section>, and found {positional.Count}. The feature's folder, then the section: Invoices InvoiceReminders.");
        }

        return new Request(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            options.GetValueOrDefault("--keys"),
            options.GetValueOrDefault("--root") ?? ".",
            dryRun);
    }

    private static Spec? Check(Request request, string root, List<string> problems)
    {
        string features = request.Features ?? string.Empty;
        string section = request.Section ?? string.Empty;

        if (!_pascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!_pascalCase.IsMatch(section))
        {
            problems.Add($"'{section}' is not a section name: <Section> is PascalCase, e.g. InvoiceReminders.");
        }
        else
        {
            problems.AddRange(SectionProblems(section));
        }

        var keys = request.Keys is null ? [] : ReadKeys(request.Keys, problems);

        if (request.Keys is null)
        {
            problems.Add("--keys is required and has no default. List what the section holds: --keys \"MaxItems:int(1..100)=10\".");
        }
        else if (problems.Count == 0 && keys.All(key => !key.IsValidated))
        {
            problems.Add("--keys holds only bool keys, so the section validates nothing and the host would start with any value. Add a bounded key, or leave the setting to a constant.");
        }

        if (problems.Count > 0)
        {
            return null;
        }

        string prefix;

        try
        {
            prefix = ProductPrefix(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string application = Path.Combine(root, "Src", "Application", FolderOf(prefix, "Application"));

        if (!Directory.Exists(Path.Combine(application, "Features", features)))
        {
            string known = Directory.Exists(Path.Combine(application, "Features"))
                ? string.Join(", ", Directory.EnumerateDirectories(Path.Combine(application, "Features")).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal))
                : string.Empty;

            problems.Add(
                $"No feature called {features}: 'Src/Application/{FolderOf(prefix, "Application")}/Features/{features}' does not exist. "
                + (known.Length == 0 ? "This tree has no feature yet; write one first, as docs/guides/adding-a-feature.md says." : $"The features here are {known}."));

            return null;
        }

        problems.AddRange(TakenSections(root, section));

        if (problems.Count > 0)
        {
            return null;
        }

        string manifest = $"{_manifestFolder}/{features}.json";
        string manifestFull = Path.Combine(root, manifest);

        if (File.Exists(manifestFull) && !ManifestReads(manifestFull, features))
        {
            problems.Add($"'{manifest}' does not read as the manifest of {features}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        string module = Path.Combine(application, "ApplicationModule.cs");
        bool takes = File.Exists(module) && TakesConfiguration(File.ReadAllText(module), features);

        return new Spec(prefix, features, section, keys, File.Exists(manifestFull) ? manifest : null, takes);
    }

    private static List<string> SectionProblems(string section)
    {
        var problems = new List<string>();

        if (section.EndsWith("Options", StringComparison.Ordinal))
        {
            problems.Add($"'{section}' ends in 'Options', which the class name appends itself: the section is the type's name without it. Name the section {section[..^"Options".Length]}.");
        }

        if (_keywords.Contains(Lower(section), StringComparer.Ordinal))
        {
            problems.Add($"'{section}' becomes the C# keyword '{Lower(section)}' once its first letter is lowered. Choose another name.");
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

        string src = Path.Combine(root, "Src");

        foreach (string file in SourceFiles(src, "*.cs"))
        {
            foreach (Match match in _sectionName.Matches(File.ReadAllText(file)))
            {
                if (Clashes(match.Groups[1].Value))
                {
                    taken.Add($"The section {section} is already bound by {Path.GetRelativePath(root, file).Replace('\\', '/')} ({match.Groups[1].Value}). A section is written once; after that it is edited.");
                }
            }
        }

        string presentation = Path.Combine(src, "Presentation");

        foreach (string file in SourceFiles(presentation, "appsettings*.json"))
        {
            if (TopLevelNames(File.ReadAllText(file)).Any(name => string.Equals(name, section, StringComparison.OrdinalIgnoreCase)))
            {
                taken.Add($"{Path.GetRelativePath(root, file).Replace('\\', '/')} already holds a section called {section}; configuration keys are not case-sensitive.");
            }
        }

        string guide = Path.Combine(root, "docs", "reference", "configuration.md");

        if (File.Exists(guide) && File.ReadAllText(guide).Contains($"### `{section}`", StringComparison.Ordinal))
        {
            taken.Add($"docs/reference/configuration.md already has a heading for {section}.");
        }

        return [.. taken.Distinct(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> TopLevelNames(string json)
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

    private static IEnumerable<string> SourceFiles(string folder, string pattern) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            : [];

    private static bool ManifestReads(string manifestFull, string features)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(manifestFull)) is JsonObject top
                && top["feature"]?.GetValue<string>() == features
                && top["files"] is JsonArray;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static List<Key> ReadKeys(string text, List<string> problems)
    {
        var keys = new List<Key>();
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

            if (!_pascalCase.IsMatch(name))
            {
                problems.Add($"'{name}' in --keys is not a key name: PascalCase, e.g. MaxItems.");

                continue;
            }

            if (_keywords.Contains(Lower(name), StringComparer.Ordinal) || _reservedMembers.Contains(name, StringComparer.Ordinal))
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

    private static Key? ReadKey(string name, string type, Group bounds, Group defaultGroup, string entry, List<string> problems)
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

                return new Key(name, Kind.Bool, 0, 0, text);

            case "secret":
                if (bounds.Success || text is not null)
                {
                    problems.Add($"'{entry}': a secret has neither bounds nor a default, since a value in a tracked file is no secret.");

                    return null;
                }

                return new Key(name, Kind.Secret, 1, 0, null);

            case "string":
                return ReadText(name, bounds, text, entry, problems);

            case "int" or "long" or "duration":
                return ReadNumber(name, type, bounds, text, entry, problems);

            default:
                problems.Add($"'{entry}': '{type}' is not a type of this tool. The types are int, long, duration, bool, string and secret.");

                return null;
        }
    }

    private static Key? ReadText(string name, Group bounds, string? text, string entry, List<string> problems)
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

        return new Key(name, Kind.Text, min, max, text);
    }

    private static Key? ReadNumber(string name, string type, Group bounds, string? text, string entry, List<string> problems)
    {
        var kind = type switch { "int" => Kind.Int, "long" => Kind.Long, _ => Kind.Duration };

        if (!bounds.Success || !TryBounds(bounds.Value, out string low, out string high) || !TryNumber(kind, low, out long min) || !TryNumber(kind, high, out long max))
        {
            problems.Add(
                $"'{entry}': {type} needs its bounds, {type}(min..max), "
                + (kind == Kind.Duration ? "two whole-second durations such as 00:00:01..01:00:00." : "two whole numbers, neither the extreme of the type.")
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
            problems.Add($"'{entry}': the default {text} is not {(kind == Kind.Duration ? "a whole-second duration" : "a whole number")} inside {low}..{high}.");

            return null;
        }

        return new Key(name, kind, min, max, value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool TryBounds(string text, out string low, out string high)
    {
        int split = text.IndexOf("..", StringComparison.Ordinal);

        (low, high) = split > 0 ? (text[..split].Trim(), text[(split + 2)..].Trim()) : (string.Empty, string.Empty);

        return low.Length > 0 && high.Length > 0;
    }

    private static bool TryNumber(Kind kind, string text, out long value)
    {
        value = 0;

        if (kind == Kind.Duration)
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

        long extreme = kind == Kind.Int ? int.MaxValue : long.MaxValue;
        long floor = kind == Kind.Int ? int.MinValue : long.MinValue;

        return value > floor && value < extreme;
    }

    private static TimeSpan Span(long seconds) => TimeSpan.FromSeconds(seconds);

    private static string SpanExpression(long seconds) => seconds switch
    {
        _ when seconds % 86_400 == 0 => $"TimeSpan.FromDays({seconds / 86_400})",
        _ when seconds % 3_600 == 0 => $"TimeSpan.FromHours({seconds / 3_600})",
        _ when seconds % 60 == 0 => $"TimeSpan.FromMinutes({seconds / 60})",
        _ => $"TimeSpan.FromSeconds({seconds})",
    };

    private static string ProductPrefix(string root)
    {
        string projectFile = Path.Combine(root, "cleanarch.json");

        if (!File.Exists(projectFile))
        {
            throw new InvalidOperationException(
                $"No cleanarch.json in '{root}', the file that records the project's name. Run this from the root of a tree generated by this template, or name that root with --root.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(projectFile));

        return document.RootElement.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } prefix
            ? prefix
            : throw new InvalidOperationException($"'{projectFile}' records no name.");
    }

    private static string FolderOf(string prefix, string project) =>
        string.Concat(prefix.Split('.').Select(SegmentInitials)) + "." + project;

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Words(string pascal) =>
        string.Concat(pascal.Select((character, index) => char.IsUpper(character) && index > 0 ? " " + character : character.ToString()));

    private static List<Emission> Emissions(Spec spec, string root)
    {
        string application = $"Src/Application/{FolderOf(spec.Prefix, "Application")}/Features/{spec.Features}/Configuration/Options";
        var emissions = new List<Emission> { new($"{application}/{spec.Options}.cs", OptionsClass(spec)) };
        string tests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";

        if (Directory.Exists(Path.Combine(root, tests)))
        {
            emissions.Add(new Emission($"{tests}/Features/{spec.Features}/Configuration/{spec.Options}Tests.cs", TestsClass(spec)));
        }

        return emissions;
    }

    private static string OptionsClass(Spec spec)
    {
        var text = new StringBuilder();

        text.Append("using Microsoft.Extensions.Options;\n\n");
        text.Append($"namespace {spec.Namespace};\n\n");
        text.Append($"public sealed class {spec.Options}\n{{\n");
        text.Append($"    public const string SectionName = \"{spec.Section}\";\n");

        foreach (var key in spec.Keys)
        {
            string initializer = key.Kind == Kind.Text && key.Default is null ? " = string.Empty" : key.Kind == Kind.Secret ? " = string.Empty" : $" = {key.DefaultLiteral}";

            text.Append($"\n    public {key.ClrType} {key.Name} {{ get; set; }}{initializer};\n");
        }

        text.Append("}\n\n");
        text.Append($"internal sealed class {spec.Validator} : IValidateOptions<{spec.Options}>\n{{\n");
        text.Append($"    public ValidateOptionsResult Validate(string? name, {spec.Options} options)\n    {{\n");
        text.Append("        ArgumentNullException.ThrowIfNull(options);\n\n");
        text.Append("        var failures = new List<string>();\n");
        text.Append($"        string section = {spec.Options}.SectionName;\n");

        foreach (var key in spec.Keys.Where(key => key.IsValidated))
        {
            text.Append('\n').Append(Check(key));
        }

        text.Append("\n        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);\n");
        text.Append("    }\n}\n");

        return Sorted(text.ToString());
    }

    private static string Check(Key key)
    {
        (string condition, string message) = key.Kind switch
        {
            Kind.Int or Kind.Long => (
                $"options.{key.Name} is < {key.Bound(key.Min)} or > {key.Bound(key.Max)}",
                $"must be between {key.Bound(key.Min)} and {key.Bound(key.Max)}."),
            Kind.Duration => (
                $"options.{key.Name} < {key.Literal(key.Min)} || options.{key.Name} > {key.Literal(key.Max)}",
                $"must be between {key.Bound(key.Min)} and {key.Bound(key.Max)}."),
            Kind.Text => (
                $"options.{key.Name}.Length is < {key.Min} or > {key.Max}",
                $"must be between {key.Min} and {key.Max} characters{(key.IsRequired ? ", and has no default" : string.Empty)}."),
            _ => (
                $"string.IsNullOrWhiteSpace(options.{key.Name})",
                "is required and has no default: supply it as the environment variable {section}__" + key.Name + ", never in a tracked file."),
        };

        return
            $"        if ({condition})\n"
            + "        {\n"
            + $"            failures.Add($\"'{{section}}:{key.Name}' {message}\");\n"
            + "        }\n";
    }

    private static string TestsClass(Spec spec)
    {
        var text = new StringBuilder();
        string f = spec.Features;
        string options = spec.Options;
        var validated = spec.Keys.Where(key => key.IsValidated).ToList();

        text.Append($"using {spec.Namespace};\n");
        text.Append("using Microsoft.Extensions.Configuration;\n");
        text.Append("using Microsoft.Extensions.DependencyInjection;\n");
        text.Append("using Microsoft.Extensions.Options;\n");
        text.Append("using Shouldly;\nusing Xunit;\n\n");
        text.Append($"namespace {spec.Prefix}.Application.UnitTests.Features.{f}.Configuration;\n\n");
        text.Append($"public sealed class {options}Tests\n{{\n");

        foreach (var key in validated)
        {
            text.Append(KeyTests(spec, key));
        }

        text.Append(CompositionTests(spec));
        text.Append($"    private static {options} ValidOptions() =>\n        new()");

        string[] required = [.. spec.Keys.Where(key => key.IsRequired).Select(key => $"{key.Name} = {ValidRequired(key)}")];

        text.Append(required.Length == 0 ? ";\n" : "\n        {\n" + string.Join(string.Empty, required.Select(line => $"            {line},\n")) + "        };\n");
        text.Append($"\n    private static ValidateOptionsResult Validate(Action<{options}> change)\n    {{\n");
        text.Append($"        var options = ValidOptions();\n        change(options);\n\n");
        text.Append($"        return new {spec.Validator}().Validate(name: null, options);\n    }}\n}}\n");

        return Sorted(text.ToString());
    }

    private static string ValidRequired(Key key) => key.Kind == Kind.Secret ? "\"a-secret\"" : $"new string('a', {key.Min})";

    private static string KeyTests(Spec spec, Key key)
    {
        string name = key.Name;
        string name2 = $"{spec.Section}:{name}";

        if (key.Kind == Kind.Secret)
        {
            return
                "    [Fact]\n"
                + $"    public void {name}_WhenPresent_Passes() =>\n        Validate(options => options.{name} = \"a-secret\").Succeeded.ShouldBeTrue();\n\n"
                + "    [Theory]\n    [InlineData(\"\")]\n    [InlineData(\"   \")]\n"
                + $"    public void {name}_WhenBlank_NamesTheKey(string value)\n    {{\n        var result = Validate(options => options.{name} = value);\n\n        result.Failed.ShouldBeTrue();\n        result.FailureMessage.ShouldContain(\"{name2}\");\n    }}\n\n";
        }

        string suffix = key.Kind == Kind.Long ? "L" : string.Empty;
        string parameter = key.Kind switch { Kind.Int => "int value", Kind.Long => "long value", Kind.Duration => "int seconds", _ => "int length" };
        string assign = key.Kind switch
        {
            Kind.Duration => $"options.{name} = TimeSpan.FromSeconds(seconds)",
            Kind.Text => $"options.{name} = new string('a', length)",
            _ => $"options.{name} = value",
        };
        string[] inside = [Num(key.Min) + suffix, Num(key.Max) + suffix];
        string[] outside = key.Kind == Kind.Text && key.Min < 1 ? [Num(key.Max + 1)] : [Num(key.Min - 1) + suffix, Num(key.Max + 1) + suffix];

        string Data(string[] values) => string.Concat(values.Select(value => $"    [InlineData({value})]\n"));

        return
            "    [Theory]\n" + Data(inside)
            + $"    public void {name}_AtItsBounds_Passes({parameter}) =>\n        Validate(options => {assign}).Succeeded.ShouldBeTrue();\n\n"
            + "    [Theory]\n" + Data(outside)
            + $"    public void {name}_PastItsBounds_NamesTheKey({parameter})\n    {{\n        var result = Validate(options => {assign});\n\n        result.Failed.ShouldBeTrue();\n        result.FailureMessage.ShouldContain(\"{name2}\");\n    }}\n\n";
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string CompositionTests(Spec spec)
    {
        var text = new StringBuilder();
        string options = spec.Options;
        var broken = spec.Keys.First(key => key.IsValidated);
        string call = spec.TakesConfiguration ? $"services.Add{spec.Features}(configuration);" : $"services.AddSingleton<IConfiguration>(configuration);\n        services.Add{spec.Features}();";

        string Setting(string key, string value) => $"                [\"{spec.Section}:{key}\"] = {value},\n";

        string BrokenValue() => broken.Kind switch
        {
            Kind.Int or Kind.Long => $"\"{broken.Bound(broken.Max + 1)}\"",
            Kind.Duration => $"\"{broken.Bound(broken.Max + 1)}\"",
            Kind.Text => $"new string('a', {broken.Max + 1})",
            _ => "string.Empty",
        };

        string ValidValue(Key key) => key.Kind switch
        {
            Kind.Secret => "\"a-secret\"",
            Kind.Text => $"new string('b', {OtherLength(key)})",
            Kind.Bool => key.Default == "true" ? "\"false\"" : "\"true\"",
            _ => $"\"{key.Bound(OtherNumber(key))}\"",
        };

        text.Append("    [Fact]\n");
        text.Append($"    public void ASectionOutOfRange_StopsTheHostAtStart()\n    {{\n");
        text.Append($"        var configuration = new ConfigurationBuilder()\n            .AddInMemoryCollection(new Dictionary<string, string?>\n            {{\n");

        foreach (var key in spec.Keys.Where(key => key.IsRequired && key.Name != broken.Name))
        {
            text.Append(Setting(key.Name, ValidValue(key)));
        }

        text.Append(Setting(broken.Name, BrokenValue()));
        text.Append("            })\n            .Build();\n");
        text.Append("        var services = new ServiceCollection();\n");
        text.Append($"        {call}\n\n");
        text.Append("        using var provider = services.BuildServiceProvider();\n\n");
        text.Append("        var failure = Should.Throw<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());\n\n");
        text.Append($"        failure.ToString().ShouldContain(\"'{spec.Section}:{broken.Name}'\");\n    }}\n\n");

        text.Append("    [Fact]\n");
        text.Append("    public void ASectionInRange_IsBoundFromItsConfiguration()\n    {\n");
        text.Append("        var configuration = new ConfigurationBuilder()\n            .AddInMemoryCollection(new Dictionary<string, string?>\n            {\n");

        foreach (var key in spec.Keys)
        {
            text.Append(Setting(key.Name, ValidValue(key)));
        }

        text.Append("            })\n            .Build();\n");
        text.Append("        var services = new ServiceCollection();\n");
        text.Append($"        {call}\n\n");
        text.Append("        using var provider = services.BuildServiceProvider();\n");
        text.Append($"        var bound = provider.GetRequiredService<IOptions<{options}>>().Value;\n\n");

        foreach (var key in spec.Keys)
        {
            text.Append($"        bound.{key.Name}.ShouldBe({Expected(key)});\n");
        }

        text.Append("    }\n\n");

        return text.ToString();
    }

    private static long OtherNumber(Key key) => long.Parse(key.Default!, CultureInfo.InvariantCulture) == key.Min ? key.Max : key.Min;

    private static long OtherLength(Key key) => key.Default is null ? key.Min : key.Default.Length == key.Min ? key.Max : key.Min;

    private static string Expected(Key key) => key.Kind switch
    {
        Kind.Secret => "\"a-secret\"",
        Kind.Text => $"new string('b', {OtherLength(key)})",
        Kind.Bool => key.Default == "true" ? "false" : "true",
        Kind.Duration => SpanExpression(OtherNumber(key)),
        Kind.Long => Num(OtherNumber(key)) + "L",
        _ => Num(OtherNumber(key)),
    };

    private static string Sorted(string source)
    {
        string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings].Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    private static string UsingKey(string line) => (line.StartsWith("using System", StringComparison.Ordinal) ? "0" : "1") + line.TrimEnd(';').ToUpperInvariant();

    private static (List<Edit> Edits, List<string> Hosts, Dictionary<string, string> Documents) Edits(Spec spec, string root, List<string> problems)
    {
        var edits = new List<Edit>();
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        var hosts = new List<string>();

        string Load(string relative)
        {
            if (documents.TryGetValue(relative, out string? loaded))
            {
                return loaded;
            }

            string full = Path.Combine(root, relative);

            if (!File.Exists(full))
            {
                problems.Add($"{relative} is not there, and the setting needs it. Nothing is written until it is.");

                return string.Empty;
            }

            string text = Decode(File.ReadAllBytes(full));

            if (text.Contains('\r', StringComparison.Ordinal))
            {
                problems.Add($"{relative} has CRLF line endings; this project keeps LF. Run format-fix first.");
            }

            if (text.Contains(_conditionOpen, StringComparison.Ordinal))
            {
                problems.Add($"{relative} still holds template conditions: this is the template's own tree, not a generated project. Generate a project, then run this there.");
            }

            documents[relative] = text;

            return text;
        }

        string module = $"Src/Application/{FolderOf(spec.Prefix, "Application")}/ApplicationModule.cs";
        string moduleText = Load(module);
        if (moduleText.Contains(spec.Options, StringComparison.Ordinal))
        {
            problems.Add($"{module} already mentions {spec.Options}. A setting is written once; after that it is edited.");
        }

        edits.Add(new Edit(module, $"the registration in Add{spec.Features} of {module}", text => Register(text, spec)));
        edits.Add(new Edit(module, $"the usings of {module}", text => AddUsings(text, [spec.Namespace, "Microsoft.Extensions.DependencyInjection.Extensions", "Microsoft.Extensions.Options"])));

        string presentation = Path.Combine(root, "Src", "Presentation");
        string call = $".Add{spec.Features}(";

        foreach (string host in Directory.Exists(presentation) ? Directory.EnumerateDirectories(presentation).Order(StringComparer.Ordinal).ToArray() : [])
        {
            if (!SourceFiles(host, "*.cs").Any(file => File.ReadAllText(file).Contains(call, StringComparison.Ordinal)))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, Path.Combine(host, "appsettings.json")).Replace('\\', '/');

            hosts.Add(Path.GetFileName(host));
            Load(relative);
            edits.Add(new Edit(relative, $"the {spec.Section} section of {relative}", text => AddSection(text, spec)));
        }

        if (hosts.Count == 0)
        {
            problems.Add($"No host composes {spec.Features}: none of Src/Presentation/* calls Add{spec.Features}(), so no appsettings.json would carry the section. Register the feature in its host first, as docs/guides/adding-a-feature.md says.");
        }

        var required = spec.Keys.Where(key => key.IsRequired).ToList();

        if (required.Count > 0)
        {
            string composition = $"Tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Composition/HostComposition.cs";

            if (File.Exists(Path.Combine(root, composition)))
            {
                Load(composition);
                edits.Add(new Edit(composition, $"the ValidSettings of {composition}", text => AddValidSettings(text, spec)));
            }

            foreach (string file in new[] { "docker-compose.yml", ".env.example" })
            {
                if (!File.Exists(Path.Combine(root, file)))
                {
                    continue;
                }

                Load(file);
                edits.Add(file == ".env.example"
                    ? new Edit(file, $"the variables of {file}", text => AddEnvironment(text, spec))
                    : new Edit(file, $"the x-host-environment of {file}", text => AddCompose(text, spec)));
            }
        }

        string guide = "docs/reference/configuration.md";

        Load(guide);
        edits.Add(new Edit(guide, $"the {spec.Section} section of {guide}", text => AddGuide(text, spec)));

        string vocabulary = $"Tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Rules/LayoutConventionTests.cs";

        if (File.Exists(Path.Combine(root, vocabulary)))
        {
            Load(vocabulary);
            edits.Add(new Edit(vocabulary, $"the Configuration/Options folder word of {vocabulary}", text => AddVocabulary(text, FolderOf(spec.Prefix, "Application"))));
        }

        return (edits, hosts, documents);
    }

    private static bool TakesConfiguration(string moduleText, string features)
    {
        var signature = new Regex($@"public static IServiceCollection Add{features}\(this IServiceCollection services(?<rest>[^)]*)\)", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(moduleText);

        return signature.Success && Regex.IsMatch(signature.Groups["rest"].Value, @"IConfiguration configuration\b", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    private static string Decode(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0, bytes.Length - (bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0));

    private static string? Register(string text, Spec spec)
    {
        var signature = new Regex($@"public static IServiceCollection Add{spec.Features}\(this IServiceCollection services[^)]*\)\s*\{{\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!signature.Success)
        {
            return null;
        }

        int position = signature.Index + signature.Length;
        int guards = 0;

        while (true)
        {
            int end = text.IndexOf('\n', position);

            if (end < 0 || !Regex.IsMatch(text[position..end], @"^\s+ArgumentNullException\.ThrowIfNull\(\w+\);$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                break;
            }

            position = end + 1;
            guards++;
        }

        if (guards == 0)
        {
            return null;
        }

        string bind = spec.TakesConfiguration
            ? $".Bind(configuration.GetSection({spec.Options}.SectionName))"
            : $".BindConfiguration({spec.Options}.SectionName)";
        string block =
            $"\n        services.AddOptions<{spec.Options}>()\n"
            + $"            {bind}\n"
            + "            .ValidateOnStart();\n"
            + $"        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<{spec.Options}>, {spec.Validator}>());\n";

        return text.Insert(position, block);
    }

    private static string? AddUsings(string text, string[] namespaces)
    {
        string[] lines = text.Split('\n');
        int count = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        if (count == 0 || lines[..count].Any(line => !_usingLine.IsMatch(line)))
        {
            return null;
        }

        string[] current = lines[..count];

        if (!current.SequenceEqual(current.OrderBy(UsingKey, StringComparer.Ordinal)))
        {
            return null;
        }

        string[] added = [.. current.Concat(namespaces.Select(name => $"using {name};")).Distinct(StringComparer.Ordinal).OrderBy(UsingKey, StringComparer.Ordinal)];

        return string.Join('\n', [.. added, .. lines[count..]]);
    }

    private static string? AddValidSettings(string text, Spec spec)
    {
        const string opening = "ValidSettings { get; } =\n        new Dictionary<string, string?>(StringComparer.Ordinal)\n        {\n";
        int start = text.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        int close = text.IndexOf("\n        };\n", start + opening.Length - 1, StringComparison.Ordinal);

        if (close < 0 || text[start..close].Contains($"\"{spec.Section}:", StringComparison.Ordinal))
        {
            return null;
        }

        string entries = string.Concat(spec.Keys.Where(key => key.IsRequired).Select(key => $"            [\"{spec.Section}:{key.Name}\"] = \"{TestValue(key)}\",\n"));

        return text.Insert(close + 1, "\n" + entries);
    }

    private static string TestValue(Key key) =>
        key.Kind == Kind.Secret ? "architecture-tests-secret" : key.Min <= 18 && key.Max >= 18 ? "architecture-tests" : new string('a', (int)key.Min);

    private static string? AddCompose(string text, Spec spec)
    {
        string[] lines = text.Split('\n');
        int anchor = Array.FindIndex(lines, line => line == "x-host-environment: &host-environment");

        if (anchor < 0 || text.Contains($"{spec.Section}__", StringComparison.Ordinal))
        {
            return null;
        }

        int last = anchor;

        for (int index = anchor + 1; index < lines.Length && (lines[index].Length == 0 || lines[index].StartsWith(' ')); index++)
        {
            if (lines[index].Length > 0)
            {
                last = index;
            }
        }

        if (last == anchor)
        {
            return null;
        }

        string[] added =
        [
            string.Empty,
            .. spec.Keys.Where(key => key.IsRequired).Select(key => $"  {spec.Section}__{key.Name}: ${{{spec.Variable(key)}:?set {spec.Variable(key)} in .env}}"),
        ];

        return string.Join('\n', [.. lines[..(last + 1)], .. added, .. lines[(last + 1)..]]);
    }

    private static string? AddEnvironment(string text, Spec spec)
    {
        if (text.Length == 0 || spec.Keys.Any(key => key.IsRequired && text.Contains(spec.Variable(key) + "=", StringComparison.Ordinal)))
        {
            return null;
        }

        string entries = string.Concat(spec.Keys.Where(key => key.IsRequired).Select(key => $"{spec.Variable(key)}=\n"));

        return text.TrimEnd('\n') + $"\n\n# {spec.Section}: no default, so Compose refuses to start until each is set.\n" + entries;
    }

    private static string? AddSection(string text, Spec spec)
    {
        int close = text.LastIndexOf('}');

        if (close < 0 || text[..close].TrimEnd() is not { Length: > 0 } before || !text.TrimStart().StartsWith('{'))
        {
            return null;
        }

        var block = new StringBuilder();

        block.Append($"  \"{spec.Section}\": {{\n");
        block.Append(string.Join(",\n", spec.Keys.Select(key => $"    \"{key.Name}\": {key.JsonValue}")));
        block.Append("\n  }");

        string separator = before.EndsWith('{') ? "\n" : before.EndsWith(',') ? "\n" : ",\n";
        string result = before + separator + block + "\n" + text[close..];

        return TopLevelNames(result).Contains(spec.Section, StringComparer.Ordinal) ? result : null;
    }

    private static string? AddGuide(string text, Spec spec)
    {
        var table = new StringBuilder();

        table.Append($"### `{spec.Section}`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n");

        foreach (var key in spec.Keys)
        {
            table.Append($"| `{key.Name}` | {key.Type} | {key.DocumentedDefault} | {Notes(spec, key)} |\n");
        }

        string group = $"## {Words(spec.Features)}";
        var heading = new Regex($@"^{Regex.Escape(group)}$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text);
        int next;

        if (heading.Success)
        {
            var following = new Regex(@"^## ", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text, heading.Index + heading.Length);

            if (!following.Success)
            {
                return null;
            }

            next = following.Index;

            return text[..next].TrimEnd('\n') + "\n\n" + table.ToString().TrimEnd('\n') + "\n\n" + text[next..];
        }

        var beyond = new Regex(@"^## Beyond the sections$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text);

        return beyond.Success
            ? text[..beyond.Index] + group + "\n\n" + table + "\n" + text[beyond.Index..]
            : null;
    }

    private static string Notes(Spec spec, Key key) => key.Kind switch
    {
        Kind.Secret => $"**Required, secret.** Supplied by the environment variable `{spec.Section}__{key.Name}` (`{spec.Variable(key)}` in `.env` for Compose, `dotnet user-secrets` locally); the file keeps it empty and a blank value stops the host.",
        Kind.Text when key.Default is null => $"**Required.** {key.Min}–{key.Max} characters, from the environment variable `{spec.Section}__{key.Name}` (`{spec.Variable(key)}` in `.env` for Compose); the file holds none and a blank value stops the host.",
        Kind.Text => $"{key.Min}–{key.Max} characters.",
        Kind.Duration => $"`{key.Bound(key.Min)}`–`{key.Bound(key.Max)}`.",
        Kind.Bool => "`true` or `false`.",
        _ => $"{key.Bound(key.Min)}–{key.Bound(key.Max)}.",
    };

    private static string? AddVocabulary(string text, string applicationFolder)
    {
        var start = new Regex($@"\[""Src/Application/{Regex.Escape(applicationFolder)}""\]\s*=\s*\[\s*\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!start.Success)
        {
            return null;
        }

        int close = text.IndexOf("],", start.Index + start.Length, StringComparison.Ordinal);

        if (close < 0)
        {
            return null;
        }

        string list = text[(start.Index + start.Length)..close];

        if (list.Contains("\"Configuration/Options\"", StringComparison.Ordinal))
        {
            return text;
        }

        string[] lines = list.Split('\n');
        int index = Array.FindIndex(lines, line => Regex.Match(line, @"""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } first
            && string.CompareOrdinal(first.Groups[1].Value, "Configuration/Options") > 0);
        int at = start.Index + start.Length;

        if (index < 0)
        {
            return null;
        }

        at += string.Join('\n', lines[..index]).Length + (index > 0 ? 1 : 0);

        string indent = new([.. lines[index].TakeWhile(char.IsWhiteSpace)]);

        return text.Insert(at, $"{indent}\"Configuration/Options\",\n");
    }

    private static byte[] Bytes(string path, string content, bool bom)
    {
        string text = content.ReplaceLineEndings("\n");

        return bom || path.EndsWith(".cs", StringComparison.Ordinal)
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]
            : Encoding.UTF8.GetBytes(text);
    }

    private static string HashOf(Emission emission) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(emission.Path, emission.Content, bom: false)));

    private static Emission ManifestWith(string root, string manifest, List<Emission> plan)
    {
        var top = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(root, manifest)))!;
        var files = (JsonArray)top["files"]!;

        var entries = files
            .OfType<JsonObject>()
            .Select(file => (Path: file["path"]!.GetValue<string>(), Sha256: file["sha256"]!.GetValue<string>()))
            .Concat(plan.Select(emission => (emission.Path, Sha256: HashOf(emission))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var merged = new JsonArray();

        foreach (var (path, sha256) in entries)
        {
            merged.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        top["files"] = merged;

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            top.WriteTo(writer);
        }

        return new Emission(manifest, Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static void WriteAll(string root, List<Emission> emissions, Dictionary<string, string> changed, Emission? manifest)
    {
        var created = new List<string>();
        var directories = new List<string>();
        var originals = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            foreach (var emission in emissions)
            {
                string full = Path.Combine(root, emission.Path);
                string directory = Path.GetDirectoryName(full)!;
                var missing = new Stack<string>();

                for (string next = directory; !Directory.Exists(next); next = Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(directory);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                stream.Write(Bytes(emission.Path, emission.Content, bom: false));
                created.Add(full);
            }

            var replacements = changed.Select(pair => (pair.Key, Content: pair.Value)).ToList();

            if (manifest is not null)
            {
                replacements.Add((manifest.Path, manifest.Content));
            }

            foreach (var (relative, content) in replacements)
            {
                string full = Path.Combine(root, relative);
                byte[] before = File.ReadAllBytes(full);
                string staged = full + ".new";

                originals[full] = before;
                File.WriteAllBytes(staged, Bytes(relative, content, before is [0xEF, 0xBB, 0xBF, ..]));
                File.Move(staged, full, overwrite: true);
            }
        }
        catch
        {
            foreach (var (full, before) in originals)
            {
                File.WriteAllBytes(full, before);

                if (File.Exists(full + ".new"))
                {
                    File.Delete(full + ".new");
                }
            }

            foreach (string file in created.Where(File.Exists))
            {
                File.Delete(file);
            }

            foreach (string directory in Enumerable.Reverse(directories))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            throw;
        }
    }

    private static void Report(TextWriter output, Spec spec, List<Emission> emissions, IEnumerable<string> edited, List<string> hosts, bool dryRun)
    {
        string verb = dryRun ? "Would write" : "Wrote";

        output.WriteLine(dryRun ? "Dry run: nothing was written." : $"Wrote the {spec.Section} setting of {spec.Features}.");
        output.WriteLine();
        output.WriteLine($"{verb} {emissions.Count} file(s):");

        foreach (var emission in emissions)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would edit" : "Edited")}:");

        foreach (string path in edited.Order(StringComparer.Ordinal))
        {
            output.WriteLine($"  {path}");
        }

        output.WriteLine();
        output.WriteLine($"Hosts whose appsettings.json carries the section: {string.Join(", ", hosts)}.");
        output.WriteLine();
        output.WriteLine("What is left to you:");
        output.WriteLine($"  - docs/reference/configuration.md: say, in each Notes cell of the {spec.Section} table, what the key changes; the bounds are written.");
        output.WriteLine($"  - Inject IOptions<{spec.Options}> where the rule lives: the command's validator for a bound on a request, the use case for one that shapes the work.");

        foreach (var key in spec.Keys.Where(key => key.IsRequired))
        {
            output.WriteLine($"  - {key.Name} has no value in the file: `dotnet user-secrets set {spec.Section}:{key.Name} <value>` locally, or {spec.Variable(key)} in .env for Compose, whose .env.example now lists it empty;");
            output.WriteLine($"    the host refuses to start without it, and so does any test that starts a host: give it a value there, as ValidSettings in HostComposition does for the architecture tests.");
        }
    }

    private static int SelfTest()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "newsetting-selftest-" + Guid.CreateVersion7().ToString("N"));
        var problems = new List<string>();
        int checks = 0;

        void Expect(bool condition, string message)
        {
            checks++;

            if (!condition)
            {
                problems.Add(message);
            }
        }

        try
        {
            Directory.CreateDirectory(scratch);

            ParserCases(scratch, Expect);
            KeyCases(scratch, Expect);
            WriteCases(scratch, Expect);
            DefaultsOnlyCases(scratch, Expect);
            ConfigurationParameterCases(scratch, Expect);
            AnchorCases(scratch, Expect);
            TreeCases(scratch, Expect);
            RollbackCases(scratch, Expect);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private const string _module = "Src/Application/A.Application/ApplicationModule.cs";

    private const string _api = "Src/Presentation/A.Api/appsettings.json";

    private const string _worker = "Src/Presentation/A.Worker/appsettings.json";

    private const string _guide = "docs/reference/configuration.md";

    private const string _compose = "docker-compose.yml";

    private const string _environment = ".env.example";

    private const string _composition = "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs";

    private const string _vocabulary = "Tests/Architecture/A.Architecture.Tests/Rules/LayoutConventionTests.cs";

    private const string _validKeys = "MaxItems:int(1..100)=10, Interval:duration(00:00:01..01:00:00)=00:05:00, Label:string(1..20)=main, Owner:string(3..40), ApiKey:secret, Strict:bool=true, Cap:long(1..5000000000)=1000";

    private static string NewTree(string scratch)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        void Put(string relative, string content, bool bom = true)
        {
            string full = Path.Combine(root, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, [.. bom ? Encoding.UTF8.GetPreamble() : [], .. Encoding.UTF8.GetBytes(content)]);
        }

        Put("cleanarch.json", "{\n  \"name\": \"Acme\"\n}\n", bom: false);
        Directory.CreateDirectory(Path.Combine(root, "Src/Application/A.Application/Features/Invoices/Services"));
        Directory.CreateDirectory(Path.Combine(root, "Src/Application/A.Application/Features/Notes/Services"));
        Directory.CreateDirectory(Path.Combine(root, "Tests/Application/A.Application.UnitTests"));

        Put(
            _module,
            """
            using Acme.Application.Core.Common.Events;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Application;

            public static class ApplicationModule
            {
                public static IServiceCollection AddInvoices(this IServiceCollection services)
                {
                    ArgumentNullException.ThrowIfNull(services);

                    services.AddFeature("Invoices");

                    return services;
                }

                public static IServiceCollection AddNotes(this IServiceCollection services, IConfiguration configuration)
                {
                    ArgumentNullException.ThrowIfNull(services);
                    ArgumentNullException.ThrowIfNull(configuration);

                    services.AddFeature("Notes");

                    return services;
                }
            }

            """);
        Put("Src/Presentation/A.Api/Program.cs", "builder.Services.AddInvoices();\nbuilder.Services.AddNotes(builder.Configuration);\n");
        Put("Src/Presentation/A.Worker/Program.cs", "builder.Services.AddInvoices();\n");
        Put(_api, "{\n  \"Logging\": {\n    \"LogLevel\": {\n      \"Default\": \"Information\"\n    }\n  },\n  \"AllowedHosts\": \"*\"\n}\n");
        Put(_worker, "{\n  \"Logging\": {\n    \"LogLevel\": {\n      \"Default\": \"Information\"\n    }\n  }\n}\n");
        Put(_guide, "# Configuration\n\n## Database\n\n### `Database`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `MaxPoolSize` | int | `20` | 1–500. |\n\n## Notes\n\n### `NoteLimits`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `Max` | int | `5` | 1–9. |\n\n## Beyond the sections\n\nText.\n");
        Put(_vocabulary, "public sealed class LayoutConventionTests\n{\n    private static readonly Dictionary<string, string[]?> _vocabulary = new()\n    {\n        [\"Src/Application/A.Application\"] =\n            [\n                \"Helpers\",\n                \"Model/Dtos\", \"Model/Errors\",\n                \"Ports\", \"Services/Consumers\", \"UseCases\",\n            ],\n        [\"Src/Domain/A.Domain\"] =\n            [\"Model/Entities\"],\n    };\n}\n");
        Put(_compose, "name: acme\n\n# The settings every host reads.\nx-host-environment: &host-environment\n  ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}\n\n  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n\nservices:\n  api:\n    environment:\n      <<: *host-environment\n", bom: false);
        Put(_environment, "# Copy to .env.\nJWT_KEY=dev\n\n# Jobs\nJOBS_ENABLED=true\n", bom: false);
        Put(_composition, "internal static class HostComposition\n{\n    internal static IReadOnlyDictionary<string, string?> ValidSettings { get; } =\n        new Dictionary<string, string?>(StringComparer.Ordinal)\n        {\n            [\"Jwt:Key\"] = JwtKey,\n\n            [\"Files:Categories:document:UploadModes:0\"] = \"Direct\",\n        };\n}\n");
        Put(".feature-manifest/Invoices.json", "{\n  \"feature\": \"Invoices\",\n  \"files\": []\n}\n", bom: false);

        return root;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error);

        return (code, output.ToString(), error.ToString());
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void ExpectRefusal(string root, string[] args, string fragment, Action<bool, string> expect)
    {
        var before = Snapshot(root);
        var result = Invoke(root, args);
        string label = string.Join(' ', args);

        expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
        expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
        expect(result.Error.Contains("Nothing was written.", StringComparison.Ordinal), $"[{label}] did not say nothing was written.");
        expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
    }

    private static string Read(string root, string relative) =>
        File.Exists(Path.Combine(root, relative)) ? Decode(File.ReadAllBytes(Path.Combine(root, relative))) : string.Empty;

    private static void ParserCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string[] keys = ["--keys", "MaxItems:int(1..100)=10"];

        (string[] Args, string Fragment)[] refusals =
        [
            (["Invoices", "Limits"], "--keys is required"),
            (["Invoices", "Limits", "--keys"], "'--keys' needs a value"),
            (["Invoices", "Limits", .. keys, "--keys", "Max:bool=true"], "is given twice"),
            (["Invoices", "Limits", .. keys, "--force"], "'--force' is not an option"),
            (["Invoices", .. keys], "Expected two names"),
            (["Invoices", "Limits", "Extra", .. keys], "Expected two names"),
            (["invoices", "Limits", .. keys], "is not a feature folder"),
            (["Invoices", "limits", .. keys], "is not a section name"),
            (["Invoices", "LimitsOptions", .. keys], "ends in 'Options'"),
            (["Invoices", "Event", .. keys], "C# keyword"),
            (["Invoices", "Logging", .. keys], "already reads"),
            (["Invoices", new string('A', 61), .. keys], "longer than 60"),
            (["Gadgets", "Limits", .. keys], "No feature called Gadgets"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            ExpectRefusal(root, args, fragment, expect);
        }

        var several = Invoke(root, "invoices", "limits", "--keys", "x:nope, , Y:bool");

        expect(several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 4, "Several mistakes in one command were not all reported.");
    }

    private static void KeyCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string Keys, string Fragment)[] refused =
        [
            ("Max", "is not written Name:type"),
            ("max:int(1..2)=1", "is not a key name"),
            ("Max:float(1..2)=1", "is not a type of this tool"),
            ("Max:int=1", "needs its bounds"),
            ("Max:int(1..)=1", "needs its bounds"),
            ("Max:int(a..b)=1", "needs its bounds"),
            ("Max:int(5..1)=3", "min must be below max"),
            ("Max:int(1..9)", "needs its default"),
            ("Max:int(1..9)=10", "is not a whole number inside 1..9"),
            ("Max:int(1..9)=0", "is not a whole number inside 1..9"),
            ("Max:int(-2147483648..9)=1", "neither the extreme"),
            ("Max:int(1..2147483647)=1", "neither the extreme"),
            ("Max:int(1..9)=x", "is not a whole number inside"),
            ("Max:long(1..9)=\"1\"", "is not a whole number inside"),
            ("Span:duration(1..9)=2", "needs its bounds"),
            ("Span:duration(00:00:01..00:01:00)=00:02:00", "is not a whole-second duration inside"),
            ("Span:duration(00:00:00..00:01:00)=00:00:30", "needs its bounds"),
            ("Span:duration(00:00:01.5..00:01:00)=00:00:30", "needs its bounds"),
            ("Flag:bool", "needs its default"),
            ("Flag:bool=yes", "needs its default"),
            ("Flag:bool(1..2)=true", "has no bounds"),
            ("Token:secret=abc", "neither bounds nor a default"),
            ("Token:secret(1..5)", "neither bounds nor a default"),
            ("Name:string=abc", "needs its length range"),
            ("Name:string(0..9)", "minimum length must be 1 or more"),
            ("Name:string(1..9)=toolongdefault", "fits the length range"),
            ("Name:string(1..9)=a\"b", "letters, digits, spaces"),
            ("Name:string(1..9)=a\\b", "letters, digits, spaces"),
            ("Name:string(1..9)= a", "letters, digits, spaces"),
            ("Name:string(1..9)=a`b", "letters, digits, spaces"),
            ("Name:string(1..9)=a|b", "letters, digits, spaces"),
            ("Name:string(1..5000)=a", "max between 1 and 4096"),
            ("Max:int(1..9)=1, max:bool=true", "is not a key name"),
            ("Max:int(1..9)=1, MAX:bool=true", "more than once"),
            ("Class:int(1..9)=1", "C# keyword"),
            ("SectionName:int(1..9)=1", "C# keyword or a member"),
            ("Flag:bool=true", "holds only bool keys"),
        ];

        foreach (var (keys, fragment) in refused)
        {
            ExpectRefusal(root, ["Invoices", "Limits", "--keys", keys], fragment, expect);
        }

        (string Keys, string Json)[] accepted =
        [
            ("Max:int(-5..5)=-1", "\"Max\": -1"),
            ("Span:duration(1.00:00:00..2.00:00:00)=1.12:00:00, Flag:bool=false", "\"Span\": \"1.12:00:00\""),
            ("Name:string(1..20)=a b.c-d/e@f:g", "\"Name\": \"a b.c-d/e@f:g\""),
            ("Name:string(1..9)", "\"Name\": \"\""),
        ];

        foreach (var (keys, json) in accepted)
        {
            string tree = NewTree(scratch);
            var result = Invoke(tree, "Invoices", "Limits", "--keys", keys);

            expect(result.Code == 0, $"[--keys {keys}] was refused: {result.Error}");
            expect(Read(tree, _api).Contains(json, StringComparison.Ordinal), $"[--keys {keys}] left no {json} in the API's appsettings.json.");
        }
    }

    private static void WriteCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var dry = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys, "--dry-run");

        expect(dry.Code == 0 && dry.Output.Contains("Dry run: nothing was written.", StringComparison.Ordinal), $"The dry run failed: {dry.Error}");
        expect(dry.Output.Contains("InvoiceLimitsOptions.cs", StringComparison.Ordinal), "The dry run did not list what it would write.");
        expect(Same(before, Snapshot(root)), "The dry run changed the tree.");

        var result = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys);

        expect(result.Code == 0, $"A valid setting was refused: {result.Error}");

        string options = "Src/Application/A.Application/Features/Invoices/Configuration/Options/InvoiceLimitsOptions.cs";
        string tests = "Tests/Application/A.Application.UnitTests/Features/Invoices/Configuration/InvoiceLimitsOptionsTests.cs";
        string source = Read(root, options);

        void Holds(string text, string fragment, string what) =>
            expect(text.ReplaceLineEndings("\n").Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

        Holds(source, "public const string SectionName = \"InvoiceLimits\";", "The options class");
        Holds(source, "public int MaxItems { get; set; } = 10;", "The options class");
        Holds(source, "public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);", "The options class");
        Holds(source, "public string Label { get; set; } = \"main\";", "The options class");
        Holds(source, "public string Owner { get; set; } = string.Empty;", "The options class");
        Holds(source, "public string ApiKey { get; set; } = string.Empty;", "The options class");
        Holds(source, "public long Cap { get; set; } = 1000;", "The options class");
        Holds(source, "if (options.MaxItems is < 1 or > 100)", "The validator");
        Holds(source, "failures.Add($\"'{section}:MaxItems' must be between 1 and 100.\");", "The validator");
        Holds(source, "if (options.Interval < TimeSpan.FromSeconds(1) || options.Interval > TimeSpan.FromHours(1))", "The validator");
        Holds(source, "if (options.Owner.Length is < 3 or > 40)", "The validator");
        Holds(source, "if (string.IsNullOrWhiteSpace(options.ApiKey))", "The validator");
        Holds(source, "if (options.Cap is < 1 or > 5000000000)", "The validator");
        expect(!source.Contains("Strict", StringComparison.Ordinal) || !source.Contains("options.Strict", StringComparison.Ordinal), "A bool key was given a check.");

        string module = Read(root, _module);

        Holds(module, "using Acme.Application.Features.Invoices.Configuration.Options;\n", "The module's usings");
        Holds(module, "using Microsoft.Extensions.DependencyInjection.Extensions;\nusing Microsoft.Extensions.Options;\n", "The module's usings");
        Holds(module, "        ArgumentNullException.ThrowIfNull(services);\n\n        services.AddOptions<InvoiceLimitsOptions>()\n            .BindConfiguration(InvoiceLimitsOptions.SectionName)\n            .ValidateOnStart();\n        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<InvoiceLimitsOptions>, InvoiceLimitsOptionsValidator>());\n\n        services.AddFeature(\"Invoices\");", "The registration");

        foreach (string appsettings in new[] { _api, _worker })
        {
            string json = Read(root, appsettings);

            Holds(json, "\"InvoiceLimits\": {\n    \"MaxItems\": 10,\n    \"Interval\": \"00:05:00\",\n    \"Label\": \"main\",\n    \"Owner\": \"\",\n    \"ApiKey\": \"\",\n    \"Strict\": true,\n    \"Cap\": 1000\n  }", appsettings);

            try
            {
                using var document = JsonDocument.Parse(json);

                expect(document.RootElement.TryGetProperty("Logging", out _), $"{appsettings} lost a section it had.");
            }
            catch (JsonException exception)
            {
                expect(false, $"{appsettings} is not JSON any more: {exception.Message}");
            }
        }

        string guide = Read(root, _guide);

        Holds(guide, "## Invoices\n\n### `InvoiceLimits`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n| `MaxItems` | int | `10` | 1–100. |\n", "The configuration reference");
        Holds(guide, "| `Interval` | timespan | `00:05:00` | `00:00:01`–`01:00:00`. |", "The configuration reference");
        Holds(guide, "| `Owner` | string | — | **Required.** 3–40 characters, from the environment variable", "The configuration reference");
        Holds(guide, "| `ApiKey` | string | `\"\"` | **Required, secret.** Supplied by the environment variable `InvoiceLimits__ApiKey`", "The configuration reference");
        Holds(guide, "|\n\n## Beyond the sections", "The configuration reference");
        expect(guide.IndexOf("## Invoices", StringComparison.Ordinal) < guide.IndexOf("## Beyond the sections", StringComparison.Ordinal), "The new group is not before 'Beyond the sections'.");
        Holds(Read(root, _vocabulary), "            [\n                \"Configuration/Options\",\n                \"Helpers\",", "The folder vocabulary");

        Holds(Read(root, _composition), "            [\"Files:Categories:document:UploadModes:0\"] = \"Direct\",\n\n            [\"InvoiceLimits:Owner\"] = \"architecture-tests\",\n            [\"InvoiceLimits:ApiKey\"] = \"architecture-tests-secret\",\n        };", "The architecture tests' valid settings");
        Holds(Read(root, _compose), "  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n\n  InvoiceLimits__Owner: ${INVOICE_LIMITS_OWNER:?set INVOICE_LIMITS_OWNER in .env}\n  InvoiceLimits__ApiKey: ${INVOICE_LIMITS_API_KEY:?set INVOICE_LIMITS_API_KEY in .env}\n\nservices:", "The Compose file");
        Holds(Read(root, _environment), "JOBS_ENABLED=true\n\n# InvoiceLimits: no default, so Compose refuses to start until each is set.\nINVOICE_LIMITS_OWNER=\nINVOICE_LIMITS_API_KEY=\n", "The environment example");
        expect(!Read(root, _compose).Contains("InvoiceLimits__MaxItems", StringComparison.Ordinal), "A key with a default was mapped in Compose.");
        Holds(guide, "from the environment variable `InvoiceLimits__Owner` (`INVOICE_LIMITS_OWNER` in `.env` for Compose)", "The configuration reference");

        string testSource = Read(root, tests);

        Holds(testSource, "public void MaxItems_PastItsBounds_NamesTheKey(int value)", "The tests");
        Holds(testSource, "[InlineData(0)]\n    [InlineData(101)]", "The tests");
        Holds(testSource, "public void ApiKey_WhenBlank_NamesTheKey(string value)", "The tests");
        Holds(testSource, "public void Owner_PastItsBounds_NamesTheKey(int length)", "The tests");
        Holds(testSource, "[InlineData(2)]\n    [InlineData(41)]", "The tests");
        Holds(testSource, "[InlineData(5000000001L)]", "The tests");
        Holds(testSource, "services.AddSingleton<IConfiguration>(configuration);\n        services.AddInvoices();", "The tests");
        Holds(testSource, "failure.ToString().ShouldContain(\"'InvoiceLimits:MaxItems'\");", "The tests");
        Holds(testSource, "bound.Interval.ShouldBe(TimeSpan.FromSeconds(1));", "The tests");
        Holds(testSource, "[\"InvoiceLimits:Owner\"] = new string('b', 3),", "The tests");
        Holds(testSource, "Owner = new string('a', 3),", "The tests");
        expect(!testSource.Contains("Strict_", StringComparison.Ordinal), "A bool key was given a boundary test.");

        foreach (string file in new[] { options, tests })
        {
            foreach (string finding in FileProblems(root, file))
            {
                expect(false, finding);
            }
        }

        string manifest = Read(root, ".feature-manifest/Invoices.json");

        Holds(manifest, options, "The feature's manifest");
        Holds(manifest, tests, "The feature's manifest");

        var again = Snapshot(root);
        var second = Invoke(root, "Invoices", "InvoiceLimits", "--keys", _validKeys);

        expect(second.Code == 1 && second.Error.Contains("is already bound by", StringComparison.Ordinal), "A second run was not refused for the section it already wrote.");
        expect(Same(again, Snapshot(root)), "A second run changed the tree.");

        var other = Invoke(root, "Invoices", "invoiceLimits", "--keys", _validKeys);

        expect(other.Code == 1, "A section differing only by its first letter's case was accepted.");

        var clash = Invoke(root, "Invoices", "InvoiceLimits:Sub", "--keys", _validKeys);

        expect(clash.Code == 1, "A section name holding a colon was accepted.");

        ExpectRefusal(root, ["Invoices", "INVOICELIMITS", "--keys", "Max:int(1..9)=1"], "already holds a section called", expect);
        ExpectRefusal(root, ["Notes", "NoteLimits", "--keys", "Max:int(1..9)=1"], "already has a heading for NoteLimits", expect);
    }

    private static IEnumerable<string> FileProblems(string root, string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));

        if (bytes is not [0xEF, 0xBB, 0xBF, ..])
        {
            yield return $"{relative}: no UTF-8 BOM, which the formatting gate refuses.";
        }

        string text = Decode(bytes);

        if (text.Contains('\r', StringComparison.Ordinal) || text.EndsWith("\n\n", StringComparison.Ordinal) || !text.EndsWith('\n'))
        {
            yield return $"{relative}: not LF endings closed by exactly one newline.";
        }

        if (text.Contains("//", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none.";
        }

        string[] usings = [.. text.Split('\n').TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal))];

        if (!usings.SequenceEqual(usings.OrderBy(UsingKey, StringComparer.Ordinal)))
        {
            yield return $"{relative}: its usings are not in order.";
        }

        if (!text.Contains($"namespace Acme.{(relative.StartsWith("Tests", StringComparison.Ordinal) ? "Application.UnitTests" : "Application")}.Features.Invoices.", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }
    }

    private static void DefaultsOnlyCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(result.Code == 0, $"A setting with defaults only was refused: {result.Error}");

        foreach (string untouched in new[] { _compose, _environment, _composition })
        {
            expect(before[untouched] == Snapshot(root)[untouched], $"A setting with a default for every key rewrote {untouched}.");
        }
    }

    private static void ConfigurationParameterCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var result = Invoke(root, "Notes", "NoteRetention", "--keys", "Days:int(1..30)=7");

        expect(result.Code == 0, $"A feature taking the configuration was refused: {result.Error}");
        expect(
            Read(root, _module).Contains("            .Bind(configuration.GetSection(NoteRetentionOptions.SectionName))\n", StringComparison.Ordinal),
            "A feature that takes the configuration was not bound from it.");
        expect(!Read(root, _module).Contains("BindConfiguration", StringComparison.Ordinal), "A feature that takes the configuration was bound a second way.");
        expect(
            Read(root, "Tests/Application/A.Application.UnitTests/Features/Notes/Configuration/NoteRetentionOptionsTests.cs").Contains("services.AddNotes(configuration);", StringComparison.Ordinal),
            "The tests did not pass the configuration the feature takes.");
        expect(Read(root, _api).Contains("\"NoteRetention\"", StringComparison.Ordinal), "The only host composing Notes did not get the section.");
        expect(!Read(root, _worker).Contains("NoteRetention", StringComparison.Ordinal), "A host that does not compose the feature got its section.");
    }

    private static void AnchorCases(string scratch, Action<bool, string> expect)
    {
        string[] args = ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"];
        string[] secret = ["Invoices", "Limits", "--keys", "Max:int(1..9)=1, Token:secret"];

        (string Name, string[] Args, string File, Func<string, string> Damage, string Fragment)[] cases =
        [
            ("no method", args, _module, text => text.Replace("AddInvoices", "AddBills", StringComparison.Ordinal), "the registration in AddInvoices"),
            ("no guard", args, _module, text => text.Replace("ArgumentNullException.ThrowIfNull(services);\n\n        services.AddFeature(\"Invoices\")", "services.AddFeature(\"Invoices\")", StringComparison.Ordinal), "the registration in AddInvoices"),
            ("usings out of order", args, _module, text => text.Replace("using Acme.Application.Core.Common.Events;\nusing Microsoft.Extensions.Configuration;", "using Microsoft.Extensions.Configuration;\nusing Acme.Application.Core.Common.Events;", StringComparison.Ordinal), "the usings of"),
            ("no usings", args, _module, text => text[text.IndexOf("namespace", StringComparison.Ordinal)..], "the usings of"),
            ("not an object", args, _api, _ => "[]\n", "the Limits section of"),
            ("no guide end", args, _guide, text => text.Replace("## Beyond the sections", "## Elsewhere", StringComparison.Ordinal), "the Limits section of"),
            ("no vocabulary list", args, _vocabulary, text => text.Replace("[\"Src/Application/A.Application\"]", "[\"Src/Application/A.Elsewhere\"]", StringComparison.Ordinal), "the Configuration/Options folder word of"),
            ("no compose anchor", secret, _compose, text => text.Replace("x-host-environment: &host-environment", "x-other: &other", StringComparison.Ordinal), "the x-host-environment of"),
            ("empty compose block", secret, _compose, text => text.Replace("  ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}\n\n  Jwt__Key: ${JWT_KEY:?set JWT_KEY in .env}\n", string.Empty, StringComparison.Ordinal), "the x-host-environment of"),
            ("empty environment example", secret, _environment, _ => string.Empty, "the variables of"),
            ("no valid settings", secret, _composition, text => text.Replace("ValidSettings", "Settings", StringComparison.Ordinal), "the ValidSettings of"),
            ("crlf", args, _api, text => text.ReplaceLineEndings("\r\n"), "CRLF line endings"),
            ("missing appsettings", args, _worker, _ => string.Empty, "is not there"),
        ];

        foreach (var (name, caseArgs, file, damage, fragment) in cases)
        {
            string root = NewTree(scratch);
            string full = Path.Combine(root, file);

            if (name == "missing appsettings")
            {
                File.Delete(full);
            }
            else
            {
                byte[] bytes = File.ReadAllBytes(full);
                string damaged = damage(Decode(bytes));

                File.WriteAllBytes(full, [.. bytes is [0xEF, 0xBB, 0xBF, ..] ? Encoding.UTF8.GetPreamble() : [], .. Encoding.UTF8.GetBytes(damaged)]);
            }

            ExpectRefusal(root, caseArgs, fragment, expect);
        }
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string stranger = Path.Combine(scratch, "stranger");
        Directory.CreateDirectory(stranger);

        var elsewhere = Invoke(stranger, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("No cleanarch.json", StringComparison.Ordinal), "A tree without a project file was not refused for it.");
        expect(!Directory.EnumerateFileSystemEntries(stranger).Any(), "A refused tree was written to.");

        string unhosted = NewTree(scratch);

        File.Delete(Path.Combine(unhosted, "Src/Presentation/A.Worker/Program.cs"));
        File.WriteAllText(Path.Combine(unhosted, "Src/Presentation/A.Api/Program.cs"), "builder.Services.AddNotes(builder.Configuration);\n");
        ExpectRefusal(unhosted, ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"], "No host composes Invoices", expect);

        string template = NewTree(scratch);
        string module = Path.Combine(template, _module);

        File.WriteAllText(module, "//" + _conditionOpen + "files)\n" + File.ReadAllText(module).Replace("\uFEFF", string.Empty, StringComparison.Ordinal) + "//" + _conditionClose + "\n");
        ExpectRefusal(template, ["Invoices", "Limits", "--keys", "Max:int(1..9)=1"], "template's own tree", expect);

        string withoutTests = NewTree(scratch);

        Directory.Delete(Path.Combine(withoutTests, "Tests/Application"), recursive: true);

        var untested = Invoke(withoutTests, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(untested.Code == 0, $"A tree without an application test project was refused: {untested.Error}");
        expect(!Directory.Exists(Path.Combine(withoutTests, "Tests/Application")), "Tests were written to a tree that has no project for them.");
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        Directory.CreateDirectory(Path.Combine(root, _guide + ".new"));

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "Limits", "--keys", "Max:int(1..9)=1");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), $"A write that failed midway did not say so: {result.Error}");
        expect(Same(before, Snapshot(root)), "A write that failed midway left files or edits behind.");
    }
}

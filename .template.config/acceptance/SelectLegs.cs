#:property UseAppHost=false

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

return LegSelector.Run(args);

internal static class LegSelector
{
    private const string Usage =
        """
        Usage: dotnet run .template.config/acceptance/SelectLegs.cs -- --event <name> [--labels <json>]
                   [--changed <file>] [--output <file>]
               dotnet run .template.config/acceptance/SelectLegs.cs -- --self-test

        Chooses the acceptance legs a change can affect. It reads rules.json and matrix.json beside
        this file: each rule maps path globs to tags, or to 'none' (no leg reads the path) or 'all';
        the first rule a path matches decides, and a path no rule matches runs every leg. A leg runs
        when it carries a tag a changed path names.

        Every leg runs on an event other than pull_request, under the full-acceptance label, when
        --changed names no file (the diff could not be read) or an empty one, and when one path
        runs them all.

        --labels takes the pull request's label names as a JSON array. --output appends run, skip
        (each a matrix: {"include": [...]}), run_count and skip_count, as $GITHUB_OUTPUT reads them.

        Exit code 0 once the legs are chosen; 1 on an unreadable rules.json or matrix.json, a tag
        no leg carries, or a malformed argument.
        """;

    private const string FullAcceptanceLabel = "full-acceptance";

    private const string EveryLeg = "all";

    private const string NoLeg = "none";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private sealed record Rule(Regex[] Paths, string[] Patterns, string[] Tags);

    private sealed record Leg(string Name, string[] Tags, JsonObject Entry);

    private sealed record Selection(List<Leg> Run, List<Leg> Skip, List<string> Reasons);

    internal static int Run(string[] arguments)
    {
        if (arguments.Contains("--help") || arguments.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            string folder = OwnFolder();
            List<Rule> rules = Rules(File.ReadAllText(Path.Combine(folder, "rules.json")));
            List<Leg> legs = Legs(File.ReadAllText(Path.Combine(folder, "matrix.json")));
            RequireEveryTagOnALeg(rules, legs);

            if (arguments is ["--self-test"])
            {
                return SelfTest(rules, legs);
            }

            var options = Options(arguments);
            string eventName = options.GetValueOrDefault("--event")
                ?? throw new InvalidOperationException("--event is required.\n\n" + Usage);
            string[] labels = Labels(options.GetValueOrDefault("--labels"));
            List<string>? changed = Changed(options.GetValueOrDefault("--changed"));

            Selection selection = Select(rules, legs, eventName, labels, changed);
            Report(selection);

            if (options.GetValueOrDefault("--output") is { } output)
            {
                File.AppendAllText(output, Outputs(selection), Utf8NoBom);
            }

            return 0;
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or JsonException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static Selection Select(List<Rule> rules, List<Leg> legs, string eventName, string[] labels, List<string>? changed)
    {
        if (eventName != "pull_request")
        {
            return Everything(legs, $"A {eventName} event runs every leg.");
        }

        if (labels.Contains(FullAcceptanceLabel, StringComparer.Ordinal))
        {
            return Everything(legs, $"The {FullAcceptanceLabel} label runs every leg.");
        }

        if (changed is null)
        {
            return Everything(legs, "The diff could not be read, so every leg runs.");
        }

        if (changed.Count == 0)
        {
            return Everything(legs, "The diff names no path, so every leg runs.");
        }

        var tags = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new List<string>();

        foreach (string path in changed)
        {
            Rule? rule = rules.FirstOrDefault(candidate => candidate.Paths.Any(glob => glob.IsMatch(path)));

            if (rule is null)
            {
                return Everything(legs, $"{path} matches no rule, so every leg runs.");
            }

            if (rule.Tags.Contains(EveryLeg, StringComparer.Ordinal))
            {
                return Everything(legs, $"{path} runs every leg ({string.Join(", ", rule.Patterns)}).");
            }

            string[] named = [.. rule.Tags.Where(tag => tag != NoLeg)];
            tags.UnionWith(named);
            reasons.Add(named.Length == 0 ? $"{path}: read by no leg." : $"{path}: {string.Join(", ", named)}.");
        }

        var run = legs.Where(leg => leg.Tags.Any(tags.Contains)).ToList();
        return new Selection(run, [.. legs.Except(run)], reasons);
    }

    private static Selection Everything(List<Leg> legs, string reason) => new([.. legs], [], [reason]);

    private static void Report(Selection selection)
    {
        foreach (string reason in selection.Reasons)
        {
            Console.WriteLine(reason);
        }

        Console.WriteLine();
        Console.WriteLine($"Runs {selection.Run.Count} leg(s):");
        foreach (Leg leg in selection.Run)
        {
            Console.WriteLine($"  + {leg.Name}");
        }

        Console.WriteLine($"Skips {selection.Skip.Count} leg(s):");
        foreach (Leg leg in selection.Skip)
        {
            Console.WriteLine($"  - {leg.Name}");
        }
    }

    private static string Outputs(Selection selection)
    {
        var run = new JsonArray([.. selection.Run.Select(leg => (JsonNode)leg.Entry.DeepClone())]);
        var skip = new JsonArray([.. selection.Skip.Select(leg => (JsonNode)new JsonObject { ["name"] = leg.Name })]);

        return new StringBuilder()
            .Append("run=").Append(new JsonObject { ["include"] = run }.ToJsonString()).Append('\n')
            .Append("skip=").Append(new JsonObject { ["include"] = skip }.ToJsonString()).Append('\n')
            .Append("run_count=").Append(selection.Run.Count).Append('\n')
            .Append("skip_count=").Append(selection.Skip.Count).Append('\n')
            .ToString();
    }

    private static List<Rule> Rules(string json)
    {
        JsonArray rules = JsonNode.Parse(json)?["rules"]?.AsArray()
            ?? throw new InvalidOperationException("rules.json holds no 'rules' array.");

        return [.. rules.Select(rule =>
        {
            string[] patterns = Strings(rule?["paths"], "a rule's 'paths'");
            string[] tags = Strings(rule?["tags"], "a rule's 'tags'");

            if (patterns.Length == 0 || tags.Length == 0)
            {
                throw new InvalidOperationException("A rule in rules.json names no path or no tag.");
            }

            return new Rule([.. patterns.Select(Glob)], patterns, tags);
        })];
    }

    private static List<Leg> Legs(string json)
    {
        JsonArray legs = JsonNode.Parse(json)?.AsArray()
            ?? throw new InvalidOperationException("matrix.json holds no array of legs.");

        var read = legs.Select(node =>
        {
            JsonObject entry = node?.AsObject().DeepClone().AsObject()
                ?? throw new InvalidOperationException("A leg in matrix.json is not an object.");
            string name = entry["name"]?.GetValue<string>()
                ?? throw new InvalidOperationException("A leg in matrix.json has no name.");
            string[] tags = Strings(entry["tags"], $"the tags of '{name}'");
            entry.Remove("tags");
            return new Leg(name, tags, entry);
        }).ToList();

        string? twice = read.GroupBy(leg => leg.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (twice is not null)
        {
            throw new InvalidOperationException($"matrix.json names the leg '{twice}' twice.");
        }

        return read;
    }

    private static void RequireEveryTagOnALeg(List<Rule> rules, List<Leg> legs)
    {
        var carried = legs.SelectMany(leg => leg.Tags).ToHashSet(StringComparer.Ordinal);
        string[] orphans = [.. rules
            .SelectMany(rule => rule.Tags)
            .Where(tag => tag is not EveryLeg and not NoLeg && !carried.Contains(tag))
            .Distinct(StringComparer.Ordinal)];

        if (orphans.Length > 0)
        {
            throw new InvalidOperationException($"rules.json names tags no leg of matrix.json carries: {string.Join(", ", orphans)}.");
        }
    }

    private static string[] Strings(JsonNode? node, string what) =>
        node is JsonArray array
            ? [.. array.Select(item => item?.GetValue<string>() ?? throw new InvalidOperationException($"{what} holds a null."))]
            : throw new InvalidOperationException($"{what} is not an array of strings.");

    private static Regex Glob(string pattern)
    {
        var expression = new StringBuilder("^");

        for (int i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                bool directories = i + 2 < pattern.Length && pattern[i + 2] == '/';
                expression.Append(directories ? "(?:.*/)?" : ".*");
                i += directories ? 2 : 1;
            }
            else if (pattern[i] == '*')
            {
                expression.Append("[^/]*");
            }
            else if (pattern[i] == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(pattern[i].ToString()));
            }
        }

        return new Regex(expression.Append('$').ToString(), RegexOptions.CultureInvariant, Timeout);
    }

    private static Dictionary<string, string> Options(string[] arguments)
    {
        string[] known = ["--event", "--labels", "--changed", "--output"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int i = 0; i < arguments.Length; i++)
        {
            if (!known.Contains(arguments[i], StringComparer.Ordinal) || i + 1 >= arguments.Length)
            {
                throw new InvalidOperationException($"Unexpected argument '{arguments[i]}'.\n\n" + Usage);
            }

            options[arguments[i]] = arguments[++i];
        }

        return options;
    }

    private static string[] Labels(string? json) =>
        string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonArray labels
            ? []
            : [.. labels.Select(label => label?.GetValue<string>()).OfType<string>()];

    private static List<string>? Changed(string? file) =>
        file is null || !File.Exists(file)
            ? null
            : [.. File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0)];

    private static string OwnFolder() =>
        AppContext.GetData("EntryPointFileDirectoryPath") as string
        ?? throw new InvalidOperationException("Run this file with 'dotnet run <path>/SelectLegs.cs', which tells it where rules.json is.");

    private sealed record Case(string Label, string Event, string[] Labels, string[]? Changed, string[]? Expected);

    private static readonly string[] WebLegs =
    [
        "web placeholder, smallest project",
        "web react, local auth",
        "web angular, external auth, notes",
        "web react back office, smallest local project",
        "web react, then an angular back office",
        "no web, then a react app and its workspace",
        "web, every starter",
    ];

    private static readonly Case[] Cases =
    [
        new("a template-only page", "pull_request", [], ["docs/template/maintaining-the-template.md"], []),
        new("the launcher and a template-only gate", "pull_request", [], ["Launcher/CleanArch.Create/Program.cs", "Tools/CheckWorkflows.cs"], []),
        new("the repository README, not a web app's", "pull_request", [], ["README.md"], []),
        new("a web app's page", "pull_request", [], ["Src/Web/apps/FrameworkSite/README.md"], WebLegs),
        new("the web workflow and the webapp item", "pull_request", [], [".github/workflows/web.yml", ".template.config/webapp/.template.config/template.json"], WebLegs),
        new("the Vue starter", "pull_request", [], [".template.config/starters/vue/FrameworkSite/scaffold.json"], ["web, every starter"]),
        new("the Angular starter", "pull_request", [], [".template.config/starters/angular/FrameworkSite/scaffold.json"],
            ["web angular, external auth, notes", "web react, then an angular back office", "web, every starter"]),
        new("the Avalonia starter", "pull_request", [], [".template.config/starters/avalonia/Src/Presentation/AppTemplate.PresentationApp/Program.cs"],
            ["external auth, notes", "external auth, no examples, no files", "no web, then a react app and its workspace", "web, every starter"]),
        new("the Blazor Server starter", "pull_request", [], [".template.config/starters/blazor-server/Tests/Presentation/x.cs"],
            ["web placeholder, smallest project", "web react, local auth", "web angular, external auth, notes"]),
        new("the presentation item template", "pull_request", [], [".template.config/presentation/.template.config/template.json"],
            ["external auth, no examples, no files", "no web, then a react app and its workspace", "web, every starter"]),
        new("the SQLite variant", "pull_request", [], [".template.config/variants/database-sqlite/docker-compose.database.yml"],
            ["sqlite, defaults", "sqlite, smallest project", "sqlite, external auth, all examples"]),
        new("the SQL Server variant", "pull_request", [], [".template.config/variants/database-sqlserver/docker-compose.database.yml"],
            ["sql server, external auth, all examples"]),
        new("the feature-edit applier", "pull_request", [], [".template.config/acceptance/ApplyFeatureEdits.cs"],
            ["defaults", "local auth, no files, no ci, no sonar", "external auth, no examples, no files"]),
        new("a template-only page and the Vue starter", "pull_request", [], ["docs/template/roadmap.md", ".template.config/starters/vue/FrameworkSite/scaffold.json"],
            ["web, every starter"]),
        new("the Vue starter and the domain", "pull_request", [], [".template.config/starters/vue/FrameworkSite/scaffold.json", "Src/Domain/AppTemplate.Domain/Common/Entity.cs"], null),
        new("the domain", "pull_request", [], ["Src/Domain/AppTemplate.Domain/Common/Entity.cs"], null),
        new("a shared page", "pull_request", [], ["docs/README.md"], null),
        new("a folder named like the launcher's", "pull_request", [], ["LauncherTools/Run.cs"], null),
        new("the central package versions", "pull_request", [], ["Directory.Packages.props"], null),
        new("the template manifest", "pull_request", [], [".template.config/template.json"], null),
        new("a path no rule names", "pull_request", [], ["a-new-file.txt"], null),
        new("the selector", "pull_request", [], [".template.config/acceptance/SelectLegs.cs"], null),
        new("the rules", "pull_request", [], [".template.config/acceptance/rules.json"], null),
        new("the matrix", "pull_request", [], [".template.config/acceptance/matrix.json"], null),
        new("the acceptance workflow", "pull_request", [], [".github/workflows/template-acceptance.yml"], null),
        new("an empty diff", "pull_request", [], [], null),
        new("an unreadable diff", "pull_request", [], null, null),
        new("the full-acceptance label", "pull_request", ["documentation", FullAcceptanceLabel], ["docs/template/roadmap.md"], null),
        new("a push", "push", [], ["docs/template/roadmap.md"], null),
        new("a manual run", "workflow_dispatch", [], null, null),
    ];

    private static int SelfTest(List<Rule> rules, List<Leg> legs)
    {
        var failures = new List<string>();
        string[] everyLeg = [.. legs.Select(leg => leg.Name)];

        foreach (Case test in Cases)
        {
            Selection selection = Select(rules, legs, test.Event, test.Labels, test.Changed?.ToList());
            string[] expected = test.Expected ?? everyLeg;
            string[] ran = [.. selection.Run.Select(leg => leg.Name)];

            if (!ran.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                failures.Add($"{test.Label}: ran [{string.Join("; ", ran)}], expected [{string.Join("; ", expected)}]");
            }

            if (selection.Run.Count + selection.Skip.Count != legs.Count || selection.Run.Intersect(selection.Skip).Any())
            {
                failures.Add($"{test.Label}: the legs run and the legs skipped are not the matrix split in two");
            }
        }

        string outputs = Outputs(Select(rules, legs, "pull_request", [], ["docs/template/roadmap.md", ".template.config/starters/vue/FrameworkSite/scaffold.json"]));
        var lines = outputs.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..]);
        JsonNode? runMatrix = JsonNode.Parse(lines.GetValueOrDefault("run", "null"));
        JsonNode? skipMatrix = JsonNode.Parse(lines.GetValueOrDefault("skip", "null"));

        if (runMatrix?["include"]?.AsArray() is not [JsonObject only] || only["name"]?.GetValue<string>() != "web, every starter" || only.ContainsKey("tags")
            || only["more_apps"] is null)
        {
            failures.Add("the run output is not the one leg chosen, with its fields and without its tags");
        }

        if (skipMatrix?["include"]?.AsArray().Count != legs.Count - 1 || lines.GetValueOrDefault("run_count") != "1"
            || lines.GetValueOrDefault("skip_count") != (legs.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            failures.Add("the skip output or the counts do not match the legs chosen");
        }

        if (Labels("null").Length != 0 || Labels("[\"a\",\"full-acceptance\"]") is not ["a", FullAcceptanceLabel])
        {
            failures.Add("the labels of a push, or of a pull request, are misread");
        }

        int total = Cases.Length * 2 + 3;
        Console.WriteLine($"Self-test: {Cases.Length} change(s) against rules.json and matrix.json, and the outputs they write.");

        if (failures.Count > 0)
        {
            Console.WriteLine($"SELF-TEST FAILURES ({failures.Count} of {total}):");
            foreach (string failure in failures)
            {
                Console.WriteLine($"  ! {failure}");
            }

            return 1;
        }

        Console.WriteLine("Every change selects the legs it can affect, and every trigger to run them all runs them all.");
        return 0;
    }
}

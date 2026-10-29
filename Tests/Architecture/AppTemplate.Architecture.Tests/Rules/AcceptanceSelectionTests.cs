using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class AcceptanceSelectionTests
{
    private const string _everyLeg = "all";

    private const string _noLeg = "none";

    private static readonly string[] _requiredByTheDevelopRuleset =
    [
        "defaults",
        "auth none, smallest project",
        "external auth, notes",
        "local auth, no files, no ci, no sonar",
        "external auth, all examples",
        "external auth, no examples, no files",
        "web placeholder, smallest project",
        "web react, local auth",
        "web angular, external auth, notes",
        "web react back office, smallest local project",
        "web react, then an angular back office",
        "no web, then a react app and its workspace",
        "sql server, external auth, all examples",
        "worker, external auth, all examples",
        "sqlite, defaults",
        "sqlite, smallest project",
        "sqlite, external auth, all examples",
        "windows, defaults",
    ];

    private static readonly string[] _notRequired = ["web, every starter"];

    private const string _linux = "ubuntu-latest";

    private const string _windows = "windows-latest";

    private const string _windowsJob = "template-windows";

    private static readonly string[] _fieldsEveryLegHas = ["name", "tags", "runner"];

    private static readonly string[] _foldersOfTheirOwn = ["presentation", "webapp", "webpackage"];

    private static readonly string[] _foldersOfFolders = ["starters", "variants"];

    private static string TemplateConfig => Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config");

    private static string AcceptanceFolder => Path.Combine(TemplateConfig, "acceptance");

    private static string AcceptanceWorkflow =>
        Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".github", "workflows", "template-acceptance.yml");

    [Fact]
    public void TheLegs_AreTheChecksTheDevelopRulesetRequires_AndTheOnesItDoesNot()
    {
        string[] names = [.. Legs().Select(leg => leg.Name)];

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(names.Length, "matrix.json names a leg twice, so two jobs would report one check.");
        names.Order(StringComparer.Ordinal).ShouldBe(
            _requiredByTheDevelopRuleset.Concat(_notRequired).Order(StringComparer.Ordinal),
            "The legs of .template.config/acceptance/matrix.json differ from the checks the develop ruleset requires. Change the " +
            "ruleset on GitHub and the list in this test together, as docs/template/maintaining-the-template.md says.");
    }

    [Fact]
    public void EveryLeg_RunsOnARunnerAJobServes()
    {
        var legs = Legs();

        legs.Where(leg => leg.Runner is not (_linux or _windows)).Select(leg => $"{leg.Name}: {leg.Runner}").ShouldBeEmpty(
            $"A leg of matrix.json names a runner no job of template-acceptance.yml serves: {_linux}, the default, or {_windows}.");
        legs.ShouldContain(leg => leg.Runner == _windows, "No leg runs on Windows, where a Windows user runs the tasks.");
    }

    [Fact]
    public void AWindowsLeg_SetsOnlyTheFieldsTheWindowsJobReads()
    {
        string[] read = [.. Regex.Matches(JobBlock(File.ReadAllLines(AcceptanceWorkflow), _windowsJob), @"\bmatrix\.([a-z_]+)", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];

        read.ShouldContain("options", $"The job '{_windowsJob}' of template-acceptance.yml reads no leg's options, so this rule reads the wrong job.");

        foreach (var leg in Legs().Where(leg => leg.Runner == _windows))
        {
            leg.Entry.Select(field => field.Key).Except(_fieldsEveryLegHas.Concat(read), StringComparer.Ordinal).ShouldBeEmpty(
                $"'{leg.Name}' runs on Windows and sets fields the job '{_windowsJob}' never reads, so what they ask for would not run: " +
                "the Windows job starts no container and sets up no web workspace.");
            string[] options = Words(leg.Entry["options"]?.GetValue<string>());
            options.Zip(options.Skip(1)).Any(pair => pair.First == "--web" && pair.Second != "none").ShouldBeFalse(
                $"'{leg.Name}' runs on Windows and generates a web app, which needs the pnpm and Node.js the job '{_windowsJob}' does not set up.");
        }
    }

    [Fact]
    public void EveryFolderTheTemplateCopies_HasARuleOfItsOwn()
    {
        string[] folders =
        [
            .. _foldersOfTheirOwn.Select(folder => $".template.config/{folder}"),
            .. _foldersOfFolders.SelectMany(parent => Directory.GetDirectories(Path.Combine(TemplateConfig, parent))
                .Select(folder => $".template.config/{parent}/{Path.GetFileName(folder)}")),
        ];
        var patterns = Rules().SelectMany(rule => rule.Paths).ToHashSet(StringComparer.Ordinal);

        folders.Length.ShouldBeGreaterThan(15, "Far fewer starter and variant folders were found than .template.config holds.");
        folders.Where(folder => !patterns.Contains($"{folder}/**")).ShouldBeEmpty(
            "A folder the template copies has no rule in .template.config/acceptance/rules.json, so a change to it runs every leg: " +
            "give it a '<folder>/**' rule naming the tags of the legs that generate it, or 'all'.");
    }

    [Fact]
    public void EveryFileOfTheAcceptanceFolder_HasARuleOfItsOwn()
    {
        var patterns = Rules().SelectMany(rule => rule.Paths).ToHashSet(StringComparer.Ordinal);

        Directory.GetFiles(AcceptanceFolder)
            .Select(file => $".template.config/acceptance/{Path.GetFileName(file)}")
            .Where(file => !patterns.Contains(file))
            .ShouldBeEmpty("A file of .template.config/acceptance has no rule of its own in rules.json: the selector and its data run every leg, a tool a leg runs names that leg's tag.");
    }

    [Fact]
    public void EveryTagARuleNames_IsCarriedByALeg_AndEveryLegTag_ByARule()
    {
        var named = Rules().SelectMany(rule => rule.Tags).Where(tag => tag is not _everyLeg and not _noLeg).ToHashSet(StringComparer.Ordinal);
        var carried = Legs().SelectMany(leg => leg.Tags).ToHashSet(StringComparer.Ordinal);

        named.Except(carried).ShouldBeEmpty("rules.json names a tag no leg of matrix.json carries, so a change to its paths runs nothing.");
        carried.Except(named).ShouldBeEmpty("A leg of matrix.json carries a tag no rule of rules.json names, which selects nothing.");
    }

    [Fact]
    public void EachLegsTags_AreTheOnesItsOptionsChoose()
    {
        string[] webStarters = [.. Directory.GetDirectories(Path.Combine(TemplateConfig, "starters"))
            .Where(folder => Directory.Exists(Path.Combine(folder, "FrameworkSite")))
            .Select(folder => Path.GetFileName(folder))];
        string[] presentationStarters = [.. Directory.GetDirectories(Path.Combine(TemplateConfig, "starters"))
            .Where(folder => Directory.Exists(Path.Combine(folder, "Src", "Presentation", "AppTemplate.PresentationApp")))
            .Select(folder => Path.GetFileName(folder))];

        webStarters.ShouldContain("angular", "No web starter was found under .template.config/starters.");
        presentationStarters.ShouldContain("avalonia", "No presentation starter was found under .template.config/starters.");

        foreach (var leg in Legs())
        {
            leg.Tags.Order(StringComparer.Ordinal).ShouldBe(
                ChosenTags(leg.Entry, webStarters, presentationStarters).Order(StringComparer.Ordinal),
                $"The tags of '{leg.Name}' in matrix.json are not the ones its options choose, so a change to what it generates would skip it.");
        }
    }

    [Fact]
    public void EveryPathNoLegReads_IsLeftOutOfTheTemplatePackage()
    {
        string project = Path.Combine(TemplateConfig, "TemplatePackage.csproj");
        var packageExcludes = XDocument.Load(project)
            .Descendants("TemplateExclude")
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(pattern => pattern.Replace("$(TemplateRoot)", string.Empty, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        string[] unread = [.. Rules().Where(rule => rule.Tags.Contains(_noLeg)).SelectMany(rule => rule.Paths)];

        unread.ShouldNotBeEmpty("rules.json names no path that no leg reads.");
        unread.Where(path => !packageExcludes.Contains(path)).ShouldBeEmpty(
            "rules.json says no leg reads these paths, yet the template package carries them: add each to the TemplateExclude " +
            "items of .template.config/TemplatePackage.csproj, or give it the tags of the legs that read it.");
    }

    private static List<string> ChosenTags(JsonObject leg, string[] webStarters, string[] presentationStarters)
    {
        string? Field(string name) => leg[name]?.GetValue<string>();
        string[] options = Words(Field("options"));
        string[] firstPresentation = Words(Field("first_presentation"));
        string[] apps = [.. Words(Field("first_app")), .. Words(Field("second_app"))];
        string[] moreApps = [.. Words(Field("more_apps")).Select(app => app.Split(':')[^1])];
        var tags = new List<string>();

        if (Field("web") == "true")
        {
            tags.Add("web");
        }

        if (Field("generator") == "true")
        {
            tags.Add("generator");
        }

        if (Field("database") is { } database)
        {
            tags.Add($"database-{database}");
        }

        if (Chooses(options, "--jobs", "worker"))
        {
            tags.Add("jobs-worker");
        }

        if (firstPresentation.Length > 0)
        {
            tags.Add("presentation-item");
        }

        tags.AddRange(presentationStarters
            .Where(starter => Chooses(options, "--presentation", starter) || Chooses(firstPresentation, "--framework", starter))
            .Select(starter => $"presentation-{starter}"));

        tags.AddRange(webStarters
            .Where(starter => Chooses(options, "--web", starter)
                || Chooses(apps, "--framework", starter)
                || moreApps.Contains(starter, StringComparer.Ordinal)
                || (starter == "blazor-wasm" && Field("wasm_app") is not null))
            .Select(starter => $"starter-{starter}"));

        return tags;
    }

    private static string JobBlock(string[] workflow, string job)
    {
        var jobKey = new Regex(@"^  [a-z][a-z0-9_-]*:\s*$", RegexOptions.None, TimeSpan.FromSeconds(5));
        int start = Array.IndexOf(workflow, $"  {job}:");

        start.ShouldBeGreaterThanOrEqualTo(0, $"template-acceptance.yml has no job '{job}'.");

        return string.Join('\n', workflow.Skip(start + 1).TakeWhile(line => !jobKey.IsMatch(line)));
    }

    private static string[] Words(string? arguments) =>
        arguments?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static bool Chooses(string[] words, string option, string value) =>
        words.Zip(words.Skip(1)).Any(pair => pair.First == option && pair.Second == value);

    private sealed record Rule(string[] Paths, string[] Tags);

    private sealed record Leg(string Name, string[] Tags, string Runner, JsonObject Entry);

    private static List<Rule> Rules() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(AcceptanceFolder, "rules.json")))!["rules"]!.AsArray()
            .Select(rule => new Rule(Strings(rule!["paths"]), Strings(rule["tags"])))];

    private static List<Leg> Legs() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(AcceptanceFolder, "matrix.json")))!.AsArray()
            .Select(leg => new Leg(
                leg!["name"]!.GetValue<string>(),
                Strings(leg["tags"]),
                leg["runner"]?.GetValue<string>() ?? _linux,
                leg.AsObject()))];

    private static string[] Strings(JsonNode? node) =>
        [.. node!.AsArray().Select(item => item!.GetValue<string>())];
}

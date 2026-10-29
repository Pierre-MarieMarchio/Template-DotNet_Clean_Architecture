using System.Text.Json.Nodes;
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
    ];

    private static readonly string[] _notRequired = ["web, every starter"];

    private static readonly string[] _foldersOfTheirOwn = ["presentation", "webapp", "webpackage"];

    private static readonly string[] _foldersOfFolders = ["starters", "variants"];

    private static string TemplateConfig => Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config");

    private static string AcceptanceFolder => Path.Combine(TemplateConfig, "acceptance");

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

    private static string[] Words(string? arguments) =>
        arguments?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

    private static bool Chooses(string[] words, string option, string value) =>
        words.Zip(words.Skip(1)).Any(pair => pair.First == option && pair.Second == value);

    private sealed record Rule(string[] Paths, string[] Tags);

    private sealed record Leg(string Name, string[] Tags, JsonObject Entry);

    private static List<Rule> Rules() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(AcceptanceFolder, "rules.json")))!["rules"]!.AsArray()
            .Select(rule => new Rule(Strings(rule!["paths"]), Strings(rule["tags"])))];

    private static List<Leg> Legs() =>
        [.. JsonNode.Parse(File.ReadAllText(Path.Combine(AcceptanceFolder, "matrix.json")))!.AsArray()
            .Select(leg => new Leg(leg!["name"]!.GetValue<string>(), Strings(leg["tags"]), leg.AsObject()))];

    private static string[] Strings(JsonNode? node) =>
        [.. node!.AsArray().Select(item => item!.GetValue<string>())];
}

using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArch.Create.Manifests;
using Shouldly;
using Xunit;

namespace CleanArch.Create.Tests.Manifests;

public sealed class TemplateManifestTests
{
    private const string _itemName = "name";

    public static TheoryData<string> Kinds =>
        [TemplateKind.Project.ShortName, TemplateKind.WebApp.ShortName, TemplateKind.WebPackage.ShortName, TemplateKind.Presentation.ShortName];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheOptions_AreTheParametersTheIdeHostShows_InItsOrder(string shortName)
    {
        TemplateKind kind = Kind(shortName);
        using JsonDocument ideHost = Json(kind, "ide.host.json");
        string[] shown = [.. ideHost.RootElement.GetProperty("symbolInfo").EnumerateArray()
            .Where(symbol => symbol.GetProperty("isVisible").GetBoolean())
            .Select(symbol => symbol.GetProperty("id").GetString()!)];

        TemplateManifest manifest = Repository.Source.Manifest(kind);

        manifest.ShortName.ShouldBe(shortName);
        manifest.Options.Select(option => option.Symbol).ShouldBe(shown);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EachOption_CarriesItsChoicesDefaultAndDescription_FromTemplateJson(string shortName)
    {
        TemplateKind kind = Kind(shortName);
        using JsonDocument template = Json(kind, "template.json");
        JsonElement symbols = template.RootElement.GetProperty("symbols");

        foreach (TemplateOption option in Repository.Source.Manifest(kind).Options)
        {
            JsonElement symbol = symbols.GetProperty(option.Symbol);
            string[] choices = symbol.TryGetProperty("choices", out JsonElement declared)
                ? [.. declared.EnumerateArray().Select(choice => choice.GetProperty("choice").GetString()!)]
                : [];

            option.Choices.Select(choice => choice.Value).ShouldBe(choices, option.Symbol);
            option.DefaultValue.ShouldBe(symbol.TryGetProperty("defaultValue", out JsonElement value) ? value.GetString() : null, option.Symbol);
            option.Description.ShouldBe(symbol.TryGetProperty("description", out JsonElement description) ? description.GetString() : string.Empty, option.Symbol);
            option.Choices.ShouldAllBe(choice => choice.Description.Length > 0, $"{option.Symbol} has a choice without a description to show.");
        }
    }

    [Fact]
    public void TheProjectOptions_TakeTheCommandLineNamesDotnetNewAccepts()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);

        manifest.Options.Single(option => option.Symbol == "webAudience").CliName.ShouldBe("web-audience");
        manifest.Options.Single(option => option.Symbol == "webName").CliName.ShouldBe("web-name");
        manifest.Options.Single(option => option.Symbol == "auth").CliName.ShouldBe("auth");
        manifest.Options.Single(option => option.Symbol == "auth").Label.ShouldBe("Authentication");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TheEmbeddedManifests_AreTheRepositoryOnes(string shortName)
    {
        TemplateKind kind = Kind(shortName);

        Summary(TemplateSource.Embedded().Manifest(kind)).ShouldBe(Summary(Repository.Source.Manifest(kind)));
    }

    [Fact]
    public void TheLauncherRefuses_ExactlyWhatTheTemplateRefuses()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);
        manifest.Computed.ShouldContainKey("refusedOptions");
        var combinations = Combinations(manifest).ToList();

        combinations.Count.ShouldBeGreaterThan(100, "Far fewer combinations were enumerated than the template's options give.");
        foreach (Dictionary<string, string> answers in combinations)
        {
            bool refusedByLauncher = manifest.RefusalsFor(answers).Count > 0;
            bool refusedByTemplate = Condition.IsTruthy(manifest.Resolve("refusedOptions", answers));

            refusedByLauncher.ShouldBe(
                refusedByTemplate,
                "The launcher's refusals and the template's refusedOptions disagree on " + string.Join(", ", answers.Select(answer => $"{answer.Key}={answer.Value}")));
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryRefusal_FiresForSomeCombination_AndExplainsItself(string shortName)
    {
        TemplateManifest manifest = Repository.Source.Manifest(Kind(shortName));
        var combinations = Combinations(manifest).ToList();

        foreach (Refusal refusal in manifest.Refusals)
        {
            combinations.ShouldContain(answers => manifest.Holds(refusal.When, answers), $"No combination of options fires {refusal.Symbol}.");
            refusal.Explanation.ShouldNotStartWith("Never set");
            manifest.ParametersBehind(refusal.When).ShouldBeSubsetOf(manifest.Options.Select(option => option.Symbol).Append(_itemName));
        }
    }

    [Fact]
    public void TheProjectRefusals_AreTheOnesTheTemplateDocuments()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);

        manifest.Refusals.Select(refusal => refusal.CliName).Order(StringComparer.Ordinal).ShouldBe(
            ["admin-web-needs-accounts", "auth-none-needs-examples-none", "auth-none-needs-files-false", "client-presentation-needs-accounts", "mcp-needs-accounts", "my-sql-not-yet-generated", "presentation-name-is-taken", "server-rendering-needs-a-server-starter", "server-rendering-with-external-auth-not-yet-generated", "wasm-web-needs-accounts", "worker-needs-server-database"]);
    }

    [Fact]
    public void TheQuestionsAskedOnlyWithAnotherAnswer_NameOptionsOfTheTemplate_AndHangOnItsNone()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.Project);
        string[] symbols = [.. manifest.Options.Select(option => option.Symbol)];

        foreach ((string symbol, Condition when) in Launcher.ProjectAskedWhen)
        {
            symbols.ShouldContain(symbol);
            string asking = manifest.ParametersBehind(when).ShouldHaveSingleItem(symbol);
            TemplateOption option = manifest.Options.Single(candidate => candidate.Symbol == asking);

            foreach (OptionChoice choice in option.Choices)
            {
                manifest.Holds(when, new Dictionary<string, string> { [asking] = choice.Value })
                    .ShouldBe(choice.Value != "none", $"{symbol} with {asking} {choice.Value}");
            }
        }
    }

    [Fact]
    public void CommandArguments_PassWhatDiffersFromADefault_AndWhatIsRequired()
    {
        TemplateManifest manifest = Repository.Source.Manifest(TemplateKind.WebApp);
        var answers = new Dictionary<string, string>
        {
            ["framework"] = "react",
            ["audience"] = "public",
            ["workspace"] = "true",
            ["scope"] = "acme-shop",
            ["auth"] = "local",
        };

        manifest.CommandArguments(answers).ShouldBe(["--framework", "react", "--workspace", "true", "--scope", "acme-shop"]);
    }

    [Fact]
    public void Version_IsTheTemplateVersionTheProjectTemplateDeclares() =>
        Repository.Source.Manifest(TemplateKind.Project).Version.ShouldNotBeNullOrWhiteSpace();

    private static TemplateKind Kind(string shortName) =>
        new[] { TemplateKind.Project, TemplateKind.WebApp, TemplateKind.WebPackage, TemplateKind.Presentation }.Single(kind => kind.ShortName == shortName);

    private static JsonDocument Json(TemplateKind kind, string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, kind.Folder, file)));

    private static string Summary(TemplateManifest manifest) =>
        string.Join(
            Environment.NewLine,
            manifest.Options.Select(option =>
                $"{option.Symbol} {option.CliName} {option.Label} {option.Kind} {option.DefaultValue} {option.RequiredWhen} "
                + string.Join('|', option.Choices.Select(choice => choice.Value)))
            .Concat(manifest.Refusals.Select(refusal => $"{refusal.Symbol} {refusal.When} {refusal.Explanation}"))
            .Append(manifest.Version ?? string.Empty));

    private static IEnumerable<string> NamedByARefusal(TemplateManifest manifest, string symbol) =>
        manifest.Refusals
            .Select(refusal => Regex.Match(refusal.When.Text, $@"\b{symbol} == ""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1)))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Take(1);

    private static IEnumerable<Dictionary<string, string>> Combinations(TemplateManifest manifest)
    {
        IEnumerable<Dictionary<string, string>> combinations = [new Dictionary<string, string>(StringComparer.Ordinal)];

        if (manifest.Refusals.Any(refusal => refusal.When.Symbols.Contains(_itemName)))
        {
            string[] names = [.. new[] { manifest.DefaultName }.Concat(NamedByARefusal(manifest, _itemName)).OfType<string>()];
            combinations = combinations.SelectMany(answers => names.Select(name => new Dictionary<string, string>(answers) { [_itemName] = name }));
        }

        foreach (TemplateOption option in manifest.Options)
        {
            string[] values = option.Kind switch
            {
                OptionKind.Choice => [.. option.Choices.Select(choice => choice.Value)],
                OptionKind.Bool => [Condition.True, Condition.False],
                OptionKind.Text => [.. new[] { option.DefaultValue }.Concat(NamedByARefusal(manifest, option.Symbol)).OfType<string>().Distinct(StringComparer.Ordinal)],
                _ => [],
            };

            if (values.Length > 0)
            {
                combinations = combinations.SelectMany(answers => values.Select(value => new Dictionary<string, string>(answers) { [option.Symbol] = value }));
            }
        }

        return combinations;
    }
}

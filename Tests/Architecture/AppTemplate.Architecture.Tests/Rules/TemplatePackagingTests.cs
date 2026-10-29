using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class TemplatePackagingTests
{
    private static readonly string[] _projectTypeGuids =
    [
        "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC",
        "2150E333-8FDC-42A3-9474-1A3956D46DE8",
    ];

    private static readonly string[] _projectRoots = ["Src", "Tests"];

    private static readonly Regex _guid = new(
        "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void EveryGuidInTheSolution_IsRegeneratedOnGeneration()
    {
        var declared = DeclaredGuids();
        var inSolution = SolutionGuids();

        inSolution.Count.ShouldBeGreaterThanOrEqualTo(
            20,
            "Far fewer guids were found in the solution than it holds, so this rule is reading the " +
            "wrong file or failing to parse it.");

        inSolution
            .Except(declared, StringComparer.OrdinalIgnoreCase)
            .Except(_projectTypeGuids, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A guid in the solution is not in the template manifest's 'guids' list, so two " +
                "projects generated from this template would share it. Add it there.");
    }

    [Fact]
    public void NoDeclaredGuid_IsAbsentFromTheSolution()
    {
        var declared = DeclaredGuids();

        declared.ShouldNotBeEmpty("The manifest declares no guids, which cannot be right.");

        declared
            .Except(SolutionGuids(), StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "The template manifest lists a guid the solution does not contain. It was left " +
                "behind by a removed project, and a stale list is one nobody trusts.");
    }

    [Fact]
    public void TheProjectTypeGuids_AreNotRegenerated()
    {
        var declared = DeclaredGuids();

        foreach (string typeGuid in _projectTypeGuids)
        {
            declared.ShouldNotContain(
                typeGuid,
                StringComparer.OrdinalIgnoreCase,
                $"{typeGuid} identifies a kind of project, not an instance of one.");
        }
    }

    [Fact]
    public void EveryStorageProjectLine_InTheSolution_IsConditionedOnFiles() =>
        AssertEveryProjectLineIsConditioned(".Storage", "#" + "if (files)", expectedProjects: 2);

    [Fact]
    public void EveryAuthProjectLine_InTheSolution_IsConditionedOnLocalAuth() =>
        AssertEveryProjectLineIsConditioned(".Auth", "#" + "if (localAuth)", expectedProjects: 5);

    [Fact]
    public void EveryClientProjectLine_InTheSolution_IsConditionedOnDotnetClient() =>
        AssertEveryProjectLineIsConditioned(".Client", "#" + "if (dotnetClient)", expectedProjects: 2);

    private static void AssertEveryProjectLineIsConditioned(string marker, string opening, int expectedProjects)
    {
        string closing = "#" + "endif";

        string[] lines = File.ReadAllLines(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "AppTemplate.sln"));
        var guids = lines
            .Where(line => line.StartsWith("Project(", StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal))
            .Select(line => _guid.Matches(line)[^1].Value)
            .ToList();

        guids.Count.ShouldBe(expectedProjects, $"{expectedProjects} projects named '{marker}' were expected in the solution.");

        var unconditioned = new List<string>();
        bool inBlock = false;
        foreach (string line in lines)
        {
            if (line == opening)
            {
                inBlock = true;
            }
            else if (line == closing)
            {
                inBlock = false;
            }
            else if (!inBlock && guids.Any(guid => line.Contains(guid, StringComparison.OrdinalIgnoreCase)))
            {
                unconditioned.Add(line.Trim());
            }
        }

        unconditioned.ShouldBeEmpty($"These solution lines name a '{marker}' project outside a block opened by '{opening}'.");
    }

    [Theory]
    [InlineData("no-sonar", new[] { "dotnet-sonarscanner" })]
    [InlineData("no-client", new[] { "microsoft.openapi.kiota" })]
    [InlineData("no-sonar-no-client", new[] { "dotnet-sonarscanner", "microsoft.openapi.kiota" })]
    public void EachVariantToolManifest_IsTheToolManifestMinusItsTools(string variant, string[] removed)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var full = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json")))!;
        var withoutTools = JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, ".template.config", "variants", variant, ".config", "dotnet-tools.json")))!;

        foreach (string tool in removed)
        {
            full["tools"]!.AsObject().Remove(tool).ShouldBeTrue($"The tool manifest no longer declares {tool}.");
        }

        JsonNode.DeepEquals(full, withoutTools).ShouldBeTrue(
            $".template.config/variants/{variant}/.config/dotnet-tools.json has drifted from .config/dotnet-tools.json.");
    }

    [Fact]
    public void TheDependabotEntriesTheWebReadmeLists_AreTheOnesWebWrites()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var written = ConditionedBlocks(File.ReadAllLines(Path.Combine(root, ".github", "dependabot.yml")), "anyWeb");
        var listed = FencedBlocksUnder(File.ReadAllLines(Path.Combine(root, "Src", "Web", "README.md")), "- in `.github/dependabot.yml`");

        written.Count.ShouldBe(2, "dependabot.yml was expected to hold two blocks conditioned on anyWeb: the apps' images and the workspace's packages.");

        listed.ShouldBe(
            written,
            "Src/Web/README.md lists Dependabot entries for a project's first web app that differ from what --web writes " +
            "into .github/dependabot.yml. Copy the anyWeb blocks into the README, as they are.");
    }

    [Fact]
    public void ThePackage_LeavesOutWhatGenerationAlwaysLeavesOut()
    {
        var packageExcludes = PackageExcludes();
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var modifiers = manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray().ToList();

        var alwaysExcluded = modifiers
            .Where(modifier => !modifier.TryGetProperty("condition", out var condition) || condition.GetString() == "(!templateMaintenance)")
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!))
            .Where(pattern => pattern != ".template.config/**")
            .ToList();

        alwaysExcluded.Count.ShouldBeGreaterThanOrEqualTo(20, "Far fewer unconditional exclusions were read from template.json than it declares.");

        alwaysExcluded
            .Where(pattern => !packageExcludes.Any(exclude =>
                exclude == pattern || (exclude.StartsWith("**/", StringComparison.Ordinal) && pattern.EndsWith(exclude[3..], StringComparison.Ordinal))))
            .ShouldBeEmpty(
                "template.json never generates these paths, but .template.config/TemplatePackage.csproj packs them: add each to its TemplateExclude items.");
    }

    [Fact]
    public void ThePullRequestTemplate_ReachesEveryGeneratedProject()
    {
        string[] templatePaths = [".github/pull_request_template.md", ".github/**"];
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        var generationExcludes = manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray()
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!));

        generationExcludes.Intersect(templatePaths).ShouldBeEmpty(
            "template.json leaves the pull request template out of a generated project, which carries the authorship rule.");
        PackageExcludes().Any(exclude => templatePaths.Any(path => exclude.EndsWith(path, StringComparison.Ordinal))).ShouldBeFalse(
            ".template.config/TemplatePackage.csproj leaves the pull request template out of the package.");
    }

    [Fact]
    public void ThePackage_LeavesOutEveryScratchAndMaintenancePath()
    {
        string[] scratch =
        [
            ".git/**", ".claude/**", ".vs/**", "**/bin/**", "**/obj/**", "**/node_modules/**", "**/TestResults/**",
            "artifacts/**", ".template.config/TemplatePackage.csproj",
        ];

        scratch.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "A checkout can hold these paths, which the template package must not carry: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void AFolderInstall_LeavesOutEveryLocalToolFolderThePackageLeavesOut()
    {
        var localToolFolders = PackageExcludes()
            .Where(pattern => Regex.IsMatch(pattern, @"^\.[A-Za-z]\w*/\*\*$", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .ToList();

        localToolFolders.ShouldContain(".claude/**", "No dot folder was read from the TemplateExclude items, so this rule is reading the wrong file.");

        localToolFolders.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            ".template.config/TemplatePackage.csproj leaves these folders out of the package, but installing from a checkout copies them into every project: add each to the unconditional excludes of the main source of template.json.");
    }

    [Fact]
    public void TheLauncher_ReachesNeitherAProjectNorTheTemplatePackage()
    {
        const string launcher = "Launcher/**";
        var alwaysExcluded = UnconditionalExcludes();

        Directory.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Launcher")).ShouldBeTrue("The launcher's folder was not found.");
        alwaysExcluded.ShouldContain(launcher, "template.json generates the launcher into every project: add Launcher/** to its unconditional excludes.");
        PackageExcludes().ShouldContain(launcher, "The template package carries the launcher, which is a package of its own: add Launcher/** to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void TheCheckoutsMigrations_ReachNeitherAProjectNorTheTemplatePackage()
    {
        string[] migrations =
        [
            "Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Migrations/**",
            "Src/Infrastructure/AppTemplate.Infrastructure.Auth/Migrations/**",
        ];

        migrations.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "template.json generates the migrations bootstrap wrote for this checkout, which a project creates for its own model: add each to its unconditional excludes.");
        migrations.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "The template package carries the migrations bootstrap wrote for this checkout: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void OnlyTheTemplateRepository_IgnoresTheMigrations()
    {
        const string migrations = "Src/Infrastructure/*/Migrations/";
        string[] lines = File.ReadAllLines(Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".gitignore"));

        lines.Count(line => line.Trim() == migrations).ShouldBe(1, $".gitignore was expected to name {migrations} once.");
        ConditionedBlocks(lines, "templateMaintenance").ShouldContain(
            block => block.Split('\n').Any(line => line.Trim() == migrations),
            $".gitignore ignores {migrations} outside a templateMaintenance block, so a generated project would never commit the migrations bootstrap creates.");
    }

    [Fact]
    public void TheOverview_RecordsThePackageAndTheVersionGenerationWrites()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string packageId = XDocument.Load(Path.Combine(root, ".template.config", "TemplatePackage.csproj"))
            .Descendants("PackageId").Single().Value;
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        string token = manifest.RootElement.GetProperty("symbols").GetProperty("templateVersion").GetProperty("replaces").GetString()!;
        string overview = File.ReadAllText(Path.Combine(root, "docs", "getting-started", "overview.md"));

        overview.ShouldContain($"version {token} of", Case.Sensitive, "docs/getting-started/overview.md no longer holds the token templateVersion replaces.");
        overview.ShouldContain($"`{packageId}`", Case.Sensitive, "docs/getting-started/overview.md no longer names the PackageId of .template.config/TemplatePackage.csproj.");
    }

    [Theory]
    [InlineData(".template.config", "(!refusedOptions)", "dotnet run Tools/Tasks.cs bootstrap")]
    [InlineData(".template.config", "(!refusedOptions)", "the network, not Docker")]
    [InlineData(".template.config", "(!refusedOptions && scaffoldedAtBootstrap)", "scaffold.json")]
    [InlineData(".template.config", "(!refusedOptions && dotnetClient)", "the client in Src/Clients")]
    [InlineData(".template.config", "(!refusedOptions && anyWeb)", "Node.js and pnpm")]
    [InlineData(".template.config/webapp/.template.config", "(webBlazorWasm)", "web-join <name>")]
    [InlineData(".template.config/webapp/.template.config", "(webFramework)", "web-scaffold <name>")]
    [InlineData(".template.config/webapp/.template.config", "(webFramework)", "Node.js, pnpm")]
    [InlineData(".template.config/webapp/.template.config", "(webPlaceholder)", "pnpm install")]
    [InlineData(".template.config/presentation/.template.config", null, "presentation-join <project>.<name>")]
    [InlineData(".template.config/presentation/.template.config", null, "presentation-scaffold <project>.<name>")]
    [InlineData(".template.config/presentation/.template.config", "(accessKeys)", "dotnet ef migrations add AddAccessKeys")]
    public void Generation_EndsWithWhatToRunNext_ForTheShapeGenerated(string folder, string? condition, string named)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        string[] instructions = manifest.RootElement.TryGetProperty("postActions", out JsonElement actions)
            ? [.. actions.EnumerateArray()
                .Where(action => action.GetProperty("actionId").GetString() == _manualInstructions && Text(action, "condition") == condition)
                .SelectMany(action => action.GetProperty("manualInstructions").EnumerateArray())
                .Select(instruction => instruction.GetProperty("text").GetString()!)]
            : [];

        instructions.ShouldContain(
            text => text.Contains(named, StringComparison.Ordinal),
            $"{folder}/template.json prints no instruction naming '{named}' when {condition ?? "always"}, so dotnet new ends with "
            + "\"created successfully\" and nothing says what must run next, nor what it needs.");
    }

    [Fact]
    public void TheProjectsInstructions_AreLeftOutOfARefusedGeneration()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        manifest.RootElement.GetProperty("postActions").EnumerateArray()
            .Select(action => Text(action, "condition") ?? "(always)")
            .ShouldAllBe(
                condition => condition.StartsWith("(!refusedOptions", StringComparison.Ordinal),
                "A refused generation writes only RefusedGenerationOptions.cs, so an instruction to bootstrap it would be false.");
    }

    [Fact]
    public void EveryStarterHoldingAScaffold_IsOneTheBootstrapInstructionCovers()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonElement symbols = manifest.RootElement.GetProperty("symbols");
        HashSet<string> covered = Reached(symbols, ["scaffoldedAtBootstrap"]);
        string[] everyValueOf = [.. covered
            .Select(name => Text(symbols.GetProperty(name), "value") ?? string.Empty)
            .Select(value => _everyValueBut.Match(value))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)];

        string[] uncovered = [.. manifest.RootElement.GetProperty("sources").EnumerateArray()
            .Where(source => (Text(source, "source") ?? string.Empty).StartsWith("./.template.config/starters/", StringComparison.Ordinal))
            .Where(source => Directory.EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, Text(source, "source")!), _scaffoldDescriptor, SearchOption.AllDirectories).Any())
            .Select(source => Text(source, "condition")!)
            .Where(condition => !_identifier.Matches(_quoted.Replace(condition, string.Empty)).Any(name =>
                covered.Contains(name.Value)
                || Reached(symbols, [name.Value]).Where(reached => Text(symbols.GetProperty(reached), "type") == "parameter").ToArray()
                    is { Length: > 0 } parameters && parameters.All(everyValueOf.Contains)))];

        uncovered.ShouldBeEmpty(
            "These starters ship a scaffold.json bootstrap creates, under a condition scaffoldedAtBootstrap does not reach: "
            + "add their symbol to it, so dotnet new says bootstrap creates them.");
    }

    private const string _manualInstructions = "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C";

    private const string _scaffoldDescriptor = "scaffold.json";

    private static readonly Regex _everyValueBut = new(@"^\((\w+) != ""none""\)$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static HashSet<string> Reached(JsonElement symbols, IEnumerable<string> from)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(from);
        while (pending.TryPop(out string? name))
        {
            if (!symbols.TryGetProperty(name, out JsonElement symbol) || !reached.Add(name))
            {
                continue;
            }

            if (Text(symbol, "type") == "computed")
            {
                foreach (Match used in _identifier.Matches(_quoted.Replace(Text(symbol, "value") ?? string.Empty, string.Empty)))
                {
                    pending.Push(used.Value);
                }
            }
        }

        return reached;
    }

    [Theory]
    [InlineData(".template.config")]
    [InlineData(".template.config/webapp/.template.config")]
    [InlineData(".template.config/webpackage/.template.config")]
    [InlineData(".template.config/presentation/.template.config")]
    public void TheIdeHost_ShowsEveryOptionButTheRefusals(string folder)
    {
        string directory = Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "template.json")));
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ide.host.json")));

        var parameters = manifest.RootElement.GetProperty("symbols").EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "parameter")
            .ToList();
        var options = parameters.Where(symbol => !IsRefusal(symbol.Value)).Select(symbol => symbol.Name).Order(StringComparer.Ordinal).ToList();
        var shown = host.RootElement.GetProperty("symbolInfo").EnumerateArray()
            .Where(symbol => symbol.GetProperty("isVisible").GetBoolean())
            .Select(symbol => symbol.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        host.RootElement.GetProperty("defaultSymbolVisibility").GetBoolean().ShouldBeFalse(
            $"{folder}/ide.host.json shows every symbol by default, the refusals among them.");
        shown.ShouldBe(options, $"{folder}/ide.host.json must show each option of its template.json and no refusal.");
        File.Exists(Path.Combine(directory, host.RootElement.GetProperty("icon").GetString()!)).ShouldBeTrue(
            $"{folder}/ide.host.json names an icon that is not beside it.");
    }

    [Theory]
    [InlineData("sqlserver", "sqlServer")]
    [InlineData("mysql", "mySql")]
    [InlineData("sqlite", "sqlite")]
    public void ADatabase_IsRefusedExactlyUntilItsOverlayIsWired(string choice, string symbol)
    {
        string overlay = $".template.config/variants/database-{choice}/";
        bool overlaid = File.Exists(Path.Combine(
            ProjectReferenceGraph.RepositoryRoot,
            overlay,
            "Src/Infrastructure/AppTemplate.Infrastructure.Core/Common/Contexts/DatabaseProvider.cs"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string condition = $"(database == \"{choice}\")";

        bool refused = symbols.EnumerateObject().Any(candidate =>
            IsRefusal(candidate.Value)
            && candidate.Value.TryGetProperty("isRequired", out var required)
            && required.GetString() == condition);
        bool inRefusedOptions = _combination.Replace(symbols.GetProperty("refusedOptions").GetProperty("value").GetString()!, string.Empty)
            .Contains($"database == \"{choice}\"", StringComparison.Ordinal);
        bool sourced = manifest.RootElement.GetProperty("sources").EnumerateArray().Any(source =>
            source.TryGetProperty("source", out var path) && path.GetString() == "./" + overlay
            && source.TryGetProperty("condition", out var when) && when.GetString() == $"({symbol})");

        if (overlaid)
        {
            refused.ShouldBeFalse($"{overlay} holds the {choice} overlay, so template.json must stop refusing --database {choice}: remove its refusal symbol and its dotnetcli.host.json entry.");
            inRefusedOptions.ShouldBeFalse($"{overlay} holds the {choice} overlay, so refusedOptions must stop naming --database {choice}.");
            sourced.ShouldBeTrue($"{overlay} holds the {choice} overlay, but no source of template.json copies it under the condition ({symbol}).");
        }
        else
        {
            refused.ShouldBeTrue($"No overlay for {choice} exists under {overlay}, so --database {choice} must be refused by a symbol required when {condition}.");
            inRefusedOptions.ShouldBeTrue($"No overlay for {choice} exists, so refusedOptions must name --database {choice}.");
            sourced.ShouldBeFalse($"template.json copies {overlay}, which holds no overlay.");
        }
    }

    [Theory]
    [InlineData(".template.config", "web", "./.template.config/starters/")]
    [InlineData(".template.config/webapp/.template.config", "framework", "../starters/")]
    public void EveryFrameworkChoice_IsAStarterTheTemplateCopiesByIt(string folder, string option, string startersSource)
    {
        string starters = StartersFolder;
        string[] folders = WebStarters();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string[] frameworks = [.. symbols.GetProperty(option).GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString()!)
            .Where(choice => choice is not "none" and not "placeholder")
            .Order(StringComparer.Ordinal)];
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        folders.ShouldBeSubsetOf(frameworks, $"Each folder under .template.config/starters is named after the --{option} value it creates.");

        foreach (string framework in frameworks)
        {
            string[] chosenBy = [.. computed.Where(symbol => ChosenValues(symbol.Value, option).Contains(framework)).Select(symbol => symbol.Key)];
            chosenBy.Length.ShouldBe(1, $"Exactly one computed symbol of {folder}/template.json must hold ({option} == \"{framework}\"), the one that copies its starter.");
            string symbol = chosenBy[0];
            string[] copied = [.. sources
                .Where(source => Text(source, "target") == "./Src/Web/apps/"
                    && Text(source, "condition") == $"({symbol})"
                    && Text(source, "source") is { } path && path.StartsWith(startersSource, StringComparison.Ordinal))
                .Select(source => Text(source, "source")![startersSource.Length..].TrimEnd('/'))];
            copied.Length.ShouldBe(1, $"{folder}/template.json must copy one folder of {startersSource} into ./Src/Web/apps/ under the condition ({symbol}), so --{option} {framework} gets its starter.");
            string starter = copied[0];

            folders.ShouldContain(starter, $"{folder}/template.json copies {startersSource}{starter}/, which does not exist.");
            File.Exists(Path.Combine(starters, starter, "FrameworkSite", "scaffold.json")).ShouldBeTrue(
                $".template.config/starters/{starter}/FrameworkSite holds no scaffold.json, so web-scaffold would find nothing to run.");
            starter.ShouldBe(framework, $"--{option} {framework} must copy its own starter, {startersSource}{framework}/: a framework has one value, never a second name for the same app.");
            Reaches(computed, "webFramework", symbol).ShouldBeTrue($"webFramework in {folder}/template.json must reach {symbol}, so the shared wiring comes with the {framework} starter.");
        }
    }

    [Theory]
    [InlineData(".template.config", "web", "webRendering", "./.template.config/starters/")]
    [InlineData(".template.config/webapp/.template.config", "framework", "rendering", "../starters/")]
    public void ServerRendering_IsCopiedFromTheStartersHoldingIt_AndRefusedForEveryOther(string folder, string option, string rendering, string startersSource)
    {
        string[] serverStarters = [.. WebStarters().Where(starter => File.Exists(Path.Combine(StartersFolder, starter, "ssr", "FrameworkSite", "scaffold.json")))];
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();
        string refusal = $"({rendering} == \"ssr\" && {string.Join(" && ", serverStarters.Select(starter => $"{option} != \"{starter}\""))})";

        serverStarters.ShouldNotBeEmpty("No starter under .template.config/starters holds ssr/FrameworkSite/scaffold.json, so this rule reads nothing.");
        ChoiceValues(symbols.GetProperty(rendering)).ShouldBe(["spa", "ssr"], $"--{rendering} offers spa and ssr in {folder}/template.json.");
        Facet(symbols.GetProperty(rendering), "defaultValue").ShouldBe("spa", $"--{rendering} keeps the single-page app as its default.");
        computed["webServerRendering"].ShouldContain($"{rendering} == \"ssr\"", Case.Sensitive, $"webServerRendering in {folder}/template.json must hold when --{rendering} is ssr.");
        Facet(symbols.GetProperty("serverRenderingNeedsAServerStarter"), "isRequired").ShouldBe(
            refusal,
            $"{folder}/template.json must refuse --{rendering} ssr for exactly the starters without an ssr folder, which the starters holding one are not.");

        foreach (string starter in serverStarters)
        {
            string symbol = computed.Single(pair => ChosenValues(pair.Value, option).Contains(starter)).Key;
            var spa = sources.Single(source => Text(source, "source") == $"{startersSource}{starter}/");
            string[] excluded = [.. spa.GetProperty("modifiers").EnumerateArray()
                .Select(modifier => $"{Text(modifier, "condition")}:{string.Join(",", modifier.GetProperty("exclude").EnumerateArray().Select(path => path.GetString()))}")];

            excluded.ShouldBe([":ssr/**", "(webServerRendering):FrameworkSite/**"], $"{folder}/template.json must copy {startersSource}{starter}/ without its ssr folder, and without its FrameworkSite under webServerRendering, since the ssr folder holds the app's FrameworkSite then.");
            sources.Count(source => Text(source, "source") == $"{startersSource}{starter}/ssr/"
                    && Text(source, "target") == "./Src/Web/apps/"
                    && Text(source, "condition") == $"({symbol} && webServerRendering)")
                .ShouldBe(1, $"{folder}/template.json must copy {startersSource}{starter}/ssr/ into ./Src/Web/apps/ under ({symbol} && webServerRendering).");
        }
    }

    [Theory]
    [InlineData(".template.config", "presentation", "./.template.config/starters/")]
    [InlineData(".template.config/presentation/.template.config", "framework", "../starters/")]
    public void EveryPresentationChoice_IsAStarterTheTemplateCopiesByIt(string folder, string option, string startersSource)
    {
        string[] folders = PresentationStarters();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string[] presentations = [.. symbols.GetProperty(option).GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString()!)
            .Where(choice => choice is not "none")
            .Order(StringComparer.Ordinal)];
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        folders.ShouldBe(presentations, $"Each --{option} value of {folder}/template.json but none is a folder under .template.config/starters holding the project's scaffold.json, and each such folder is a value.");

        foreach (string presentation in presentations)
        {
            string[] chosenBy = [.. computed.Where(symbol => ChosenValues(symbol.Value, option).Contains(presentation)).Select(symbol => symbol.Key)];
            chosenBy.Length.ShouldBe(1, $"Exactly one computed symbol of {folder}/template.json must hold ({option} == \"{presentation}\"), the one that copies its starter.");
            sources.Count(source => Text(source, "source") == $"{startersSource}{presentation}/"
                    && Text(source, "target") == "./"
                    && Text(source, "condition") == $"({chosenBy[0]})")
                .ShouldBe(1, $"{folder}/template.json must copy {startersSource}{presentation}/ into ./ under the condition ({chosenBy[0]}).");

            using var descriptor = JsonDocument.Parse(File.ReadAllText(PresentationDescriptor(presentation)));
            switch (Text(descriptor.RootElement, "kind"))
            {
                case "dotnet":
                    Text(descriptor.RootElement, "template").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter runs a dotnet new template, which its scaffold.json must name.");
                    break;
                case "written":
                    Text(descriptor.RootElement, "generator").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter is written whole, and its scaffold.json names the generator whose shape it follows.");
                    Text(descriptor.RootElement, "version").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter is written whole, and its scaffold.json names the version of the generator it follows.");
                    descriptor.RootElement.TryGetProperty("template", out _).ShouldBeFalse($"The {presentation} starter is written whole, and its scaffold.json names no template to run.");
                    File.Exists(Path.Combine(Path.GetDirectoryName(PresentationDescriptor(presentation))!, "AppTemplate.PresentationApp.csproj"))
                        .ShouldBeTrue($"The {presentation} starter is written whole, and holds its project file.");
                    break;
                default:
                    Assert.Fail($"The {presentation} starter's scaffold.json says neither kind dotnet, for a template it runs, nor written, for a project it holds whole.");
                    break;
            }
        }
    }

    [Fact]
    public void EveryStarterFolder_IsAWebOrAPresentationStarter()
    {
        string[] all = [.. Directory.GetDirectories(StartersFolder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

        all.ShouldBe(
            [.. WebStarters().Concat(PresentationStarters()).Append(_workerStarter).Order(StringComparer.Ordinal)],
            "A folder under .template.config/starters holds FrameworkSite/scaffold.json for --web, the presentation project's scaffold.json for --presentation, or the worker's for --jobs worker; one holding none is copied by no option.");
    }

    [Fact]
    public void TheWorkerStarter_IsWrittenWhole_AndCopiedByJobsWorker()
    {
        string project = Path.Combine(StartersFolder, _workerStarter, "Src", "Presentation", "AppTemplate.Worker");
        using var descriptor = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "scaffold.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        Text(descriptor.RootElement, "kind").ShouldBe("written", "The worker's starter holds its whole project, which its scaffold.json must say, so presentation-scaffold runs no generator.");
        Text(descriptor.RootElement, "generator").ShouldNotBeNullOrWhiteSpace("A written starter names the generator whose shape it follows.");
        Text(descriptor.RootElement, "version").ShouldNotBeNullOrWhiteSpace("A written starter names the version of the generator it follows.");
        descriptor.RootElement.TryGetProperty("template", out _).ShouldBeFalse("A written starter names no template to run.");
        File.Exists(Path.Combine(project, "AppTemplate.Worker.csproj")).ShouldBeTrue("A written starter holds its project file.");
        File.Exists(Path.Combine(project, "Program.cs")).ShouldBeTrue("A written starter holds its composition root.");
        manifest.RootElement.GetProperty("sources").EnumerateArray()
            .Count(source => Text(source, "source") == $"./.template.config/starters/{_workerStarter}/"
                && Text(source, "target") == "./"
                && Text(source, "condition") == "(jobsWorker)")
            .ShouldBe(1, "template.json must copy the worker's starter into ./ under the condition (jobsWorker).");
    }

    [Fact]
    public void TheFoldersTheWorkerTakes_AreTheOnesTheApiLeavesOut()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        string[] taken = [.. MovedSources(sources)
            .SelectMany(source => source.GetProperty("include").EnumerateArray()
                .Select(include => Text(source, "source")![2..] + include.GetString()))
            .Append("docs/decisions/0011-jobs-run-in-the-api-under-a-lease.md")
            .Order(StringComparer.Ordinal)];
        string[] leftOut = [.. sources[0].GetProperty("modifiers").EnumerateArray()
            .Where(modifier => Text(modifier, "condition") == "(jobsWorker)")
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(path => path.GetString()!))
            .Order(StringComparer.Ordinal)];

        taken.Length.ShouldBeGreaterThanOrEqualTo(4, "Far fewer moved folders were found than the worker takes, so this rule is not reading template.json.");
        leftOut.ShouldBe(taken, "Under jobsWorker the API's source must leave out exactly the folders the worker's sources copy, or a job would run in both hosts or in neither.");
    }

    [Fact]
    public void EveryNamespaceTheWorkerTakes_HasItsSwitchSymbol()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        var switched = symbols.EnumerateObject()
            .Where(symbol => symbol.Name.StartsWith("jobsNamespace", StringComparison.Ordinal))
            .ToDictionary(
                symbol => Text(symbol.Value, "replaces")!,
                symbol => symbol.Value.GetProperty("parameters").GetProperty("cases").EnumerateArray()
                    .Single(@case => Text(@case, "condition") == "(jobsWorker)").GetProperty("value").GetString()!,
                StringComparer.Ordinal);

        string[] declared = [.. MovedSources(manifest.RootElement.GetProperty("sources").EnumerateArray().ToList())
            .SelectMany(MovedFiles)
            .SelectMany(File.ReadLines)
            .Select(line => _declaredNamespace.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        declared.Length.ShouldBeGreaterThanOrEqualTo(5, "Far fewer namespaces were found in the moved folders than they declare, so this rule is not reading them.");
        declared.Where(space => !switched.ContainsKey(space)).ShouldBeEmpty(
            "A namespace declared in a folder --jobs worker moves needs a generated switch symbol of template.json that replaces it, or the moved file keeps the API's namespace in the worker.");
        declared.Where(space => switched.TryGetValue(space, out string? worker) && worker != "Worker." + space["Api.".Length..]).ShouldBeEmpty(
            "Each switch symbol of a moved namespace gives the worker the same namespace with Worker in place of Api.");
    }

    [Fact]
    public void ThePresentationNameRefusal_NamesEveryProjectTheTemplateWrites()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string refusal = symbols.GetProperty("presentationNameIsTaken").GetProperty("isRequired").GetString()!;
        string refused = symbols.GetProperty("refusedOptions").GetProperty("value").GetString()!;
        const string prefix = "AppTemplate.";

        string[] projects = [.. _projectRoots
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, root), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .Order(StringComparer.Ordinal)];

        projects.Length.ShouldBeGreaterThanOrEqualTo(10, "Far fewer projects were found than this template writes, so the walk is not reading its tree.");
        projects
            .Where(project => !refusal.Contains($"presentationName == \"{project}\"", StringComparison.Ordinal))
            .ShouldBeEmpty("--presentation-name must be refused when <Project>.<name> or its test project would be one of the template's projects, which generation would overwrite: add the name to presentationNameIsTaken.");
        refused.ShouldContain(refusal, Case.Sensitive, "refusedOptions must hold the presentation name refusal, so the #error variant catches a hand-passed bypass.");

        using var item = JsonDocument.Parse(File.ReadAllText(PresentationItemManifestPath()));
        string itemRefusal = item.RootElement.GetProperty("symbols").GetProperty("presentationNameIsTaken").GetProperty("isRequired").GetString()!;
        projects
            .Where(project => !itemRefusal.Contains($"name == \"{project}\"", StringComparison.Ordinal))
            .ShouldBeEmpty("cleanarch-presentation must refuse each -n the project template refuses as --presentation-name: add the name to presentationNameIsTaken of .template.config/presentation/.template.config/template.json.");

        string[] namesOfACore = [.. projects
            .Where(project => project.EndsWith(_coreSuffix, StringComparison.Ordinal))
            .Select(project => project[..^_coreSuffix.Length])];

        namesOfACore.Length.ShouldBeGreaterThanOrEqualTo(4, "Far fewer .Core projects were found than this template writes, so this rule is not reading its tree.");
        namesOfACore
            .Where(name => !refusal.Contains($"presentationName == \"{name}\"", StringComparison.Ordinal) || !itemRefusal.Contains($"name == \"{name}\"", StringComparison.Ordinal))
            .ShouldBeEmpty("--presentation wpf writes <Project>.<name>.Core beside the app, so a name whose .Core is one of the template's projects must be refused too: add it to presentationNameIsTaken of both template.json files, under presentation == \"wpf\" or framework == \"wpf\".");
    }

    [Fact]
    public void TheWpfStartersDesktopSession_IsTheAvaloniaStartersOwn_InTheCoresNamespace()
    {
        string[] avalonia = DesktopSessionCode("avalonia");
        string[] wpf = DesktopSessionCode("wpf");

        avalonia.Length.ShouldBeGreaterThanOrEqualTo(10, "Far fewer files were found than the Avalonia starter's session and its tests hold, so this rule is not reading them.");
        wpf.ShouldBe(
            [.. avalonia.Select(path => path.Replace(_desktopProject, _desktopProject + _coreSuffix, StringComparison.Ordinal))],
            "The WPF starter's .Core holds a copy of the Avalonia starter's desktop session and its tests, file for file. Change both copies together; a third desktop client is the time to move them into a project of their own.");

        foreach ((string from, string to) in avalonia.Zip(wpf))
        {
            File.ReadAllText(Path.Combine(StartersFolder, "wpf", to)).ShouldBe(
                File.ReadAllText(Path.Combine(StartersFolder, "avalonia", from)).Replace(_desktopProject + ".", _desktopProject + _coreSuffix + ".", StringComparison.Ordinal),
                $"wpf/{to} differs from avalonia/{from} in more than the namespace: change both copies together.");
        }
    }

    [Fact]
    public void ThePresentationItem_ReadsTheProjectsOptionsAsTheProjectTemplateDeclaresThem()
    {
        using var project = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        using var item = JsonDocument.Parse(File.ReadAllText(PresentationItemManifestPath()));
        var projectSymbols = project.RootElement.GetProperty("symbols");
        var itemSymbols = item.RootElement.GetProperty("symbols");

        string[] read = [.. PresentationStarters()
            .SelectMany(starter => Directory.EnumerateFiles(Path.Combine(StartersFolder, starter), "*", SearchOption.AllDirectories))
            .Append(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "features", "presentation.md"))
            .SelectMany(File.ReadLines)
            .Select(line => _condition.Match(line))
            .Where(marker => marker.Success)
            .SelectMany(marker => _identifier.Matches(_quoted.Replace(marker.Groups[1].Value, string.Empty)).Select(name => name.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        read.Length.ShouldBeGreaterThanOrEqualTo(8, "Far fewer symbols were read from the presentation starters' markers than they use, so this rule is not reading them.");
        read.Where(symbol => !itemSymbols.TryGetProperty(symbol, out _)).ShouldBeEmpty(
            "The presentation starters or docs/features/presentation.md read these symbols, which cleanarch-presentation does not declare: declare each as the project template does.");

        foreach (var symbol in itemSymbols.EnumerateObject().Where(symbol => symbol.Name != "presentationNameIsTaken"))
        {
            string type = Text(symbol.Value, "type")!;
            if (!projectSymbols.TryGetProperty(symbol.Name, out var declared) || (type == "computed" && !read.Contains(symbol.Name)))
            {
                continue;
            }

            Text(declared, "type").ShouldBe(type, $"{symbol.Name} is a {type} symbol of cleanarch-presentation and of another type in the project template.");

            foreach (string facet in (string[])["datatype", "defaultValue", "isRequired", "value", "replaces"])
            {
                if (facet is "value" or "isRequired" && ChoosesTheStarter(symbol.Value, facet))
                {
                    continue;
                }

                Facet(symbol.Value, facet).ShouldBe(Facet(declared, facet), $"cleanarch-presentation declares the {facet} of {symbol.Name} otherwise than the project template, so the starter it copies would not be the project's.");
            }

            ChoiceValues(symbol.Value).ShouldBe(ChoiceValues(declared), $"cleanarch-presentation offers other values of {symbol.Name} than the project template.");
        }

        foreach (string starter in PresentationStarters())
        {
            JsonNode.DeepEquals(StarterModifiers(project.RootElement, "./.template.config/starters/", starter), StarterModifiers(item.RootElement, "../starters/", starter)).ShouldBeTrue(
                $"cleanarch-presentation must leave out of the {starter} starter exactly what the project template leaves out, under the same conditions.");
        }
    }

    [Theory]
    [InlineData(
        "blazor-server/Src/Presentation/AppTemplate.PresentationApp/README.md",
        "anyPresentation|presentationBlazorServer",
        10,
        new[] { "docs/features/presentation.md" })]
    [InlineData(
        "avalonia/Src/Presentation/AppTemplate.PresentationApp/README.md",
        "anyPresentation|presentationAvalonia|dotnetClient",
        10,
        new[] { "docs/features/presentation.md", "docs/decisions/0023-a-dotnet-client-generated-by-kiota.md", "Src/Clients/", "Tests/Clients/", "AppTemplate.sln" })]
    [InlineData(
        "mcp/Src/Presentation/AppTemplate.PresentationApp/README.md",
        "anyPresentation|presentationMcp|accessKeys",
        10,
        new[] { "docs/features/presentation.md", "docs/decisions/0027-an-mcp-server-acts-for-the-caller-its-credential-names.md" })]
    [InlineData(
        "wpf/Src/Presentation/AppTemplate.PresentationApp/README.md",
        "anyPresentation|presentationWpf|dotnetClient",
        10,
        new[] { "docs/features/presentation.md", "docs/decisions/0023-a-dotnet-client-generated-by-kiota.md", "Src/Clients/", "Tests/Clients/", "AppTemplate.sln" })]
    [InlineData(
        "blazor-wasm/FrameworkSite/README.md",
        "webBlazorWasm|dotnetClient",
        7,
        new[]
        {
            "Src/Web/apps/FrameworkSite/", "docs/decisions/0023-a-dotnet-client-generated-by-kiota.md", "Src/Clients/", "Tests/Clients/", "AppTemplate.sln",
            "docs/getting-started/overview.md", "docs/features/web-frontend.md",
        })]
    public void TheStarterReadme_ListsEveryLineItsStarterWritesOutsideItsFolders(string readme, string symbols, int files, string[] notListed)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var listed = File.ReadLines(Path.Combine(StartersFolder, readme))
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var starterSymbol = new Regex($@"(!?)\b(?:{symbols})\b", RegexOptions.None, TimeSpan.FromSeconds(5));
        var written = new List<(string File, string Line)>();

        foreach (string path in ProjectFiles(root))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            string text = File.ReadAllText(path);
            if (!text.Contains('\0', StringComparison.Ordinal) && !notListed.Any(whole => relative.StartsWith(whole, StringComparison.Ordinal)))
            {
                written.AddRange(StarterLines(text.Split('\n'), starterSymbol).Select(line => (relative, line)));
            }
        }

        written.Select(entry => entry.File).Distinct(StringComparer.Ordinal).Count().ShouldBeGreaterThanOrEqualTo(
            files, "Far fewer files hold lines written for this starter than the template conditions, so this rule is not reading them.");
        written.Where(entry => !listed.Contains(entry.Line)).Select(entry => $"{entry.File}: {entry.Line}").ShouldBeEmpty(
            $"The project template writes these lines outside the starter's folders, and {readme} does not give them to a project " +
            "that adds it with an item template: add each to the edits under 'Joining a project generated without one'.");
    }

    private static readonly Regex _condition = new(@"#(?:if|elseif)\s*(\(.*\))", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _marker = new(@"#(?:if|elseif|else|endif)\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _quoted = new("\"[^\"]*\"", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _identifier = new(@"\b[A-Za-z_]\w*\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly string[] _outsideTheProject =
        [".git", ".template.config", "Launcher", "artifacts", "bin", "obj", "node_modules", "TestResults", ".vs", ".claude", ".idea", "template"];

    private static string StartersFolder => Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "starters");

    private static string PresentationItemManifestPath() =>
        Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "presentation", ".template.config", "template.json");

    private static IEnumerable<string> ProjectFiles(string directory) =>
        Directory.EnumerateFiles(directory).Concat(Directory.EnumerateDirectories(directory)
            .Where(folder => !_outsideTheProject.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
            .SelectMany(ProjectFiles));

    private static List<string> StarterLines(string[] lines, Regex starterSymbol)
    {
        var branches = new Stack<(string Condition, bool Otherwise)>();
        var written = new List<string>();

        foreach (string line in lines)
        {
            Match marker = _marker.Match(line);

            if (!marker.Success)
            {
                if (line.Trim().Length > 0 && branches.Any(branch => IsStarterBranch(starterSymbol, branch.Condition, branch.Otherwise)))
                {
                    written.Add(line.Trim());
                }
            }
            else if (marker.Value == "#if")
            {
                branches.Push((_condition.Match(line).Groups[1].Value, false));
            }
            else if (marker.Value == "#elseif" && branches.TryPop(out _))
            {
                branches.Push((_condition.Match(line).Groups[1].Value, false));
            }
            else if (marker.Value == "#else" && branches.TryPop(out var branch))
            {
                branches.Push((branch.Condition, true));
            }
            else
            {
                branches.TryPop(out _);
            }
        }

        return written;
    }

    private static bool IsStarterBranch(Regex starterSymbol, string condition, bool otherwise) =>
        starterSymbol.Match(condition) is { Success: true } match && (match.Groups[1].Value == "!") == otherwise;

    private static string? Facet(JsonElement symbol, string facet) =>
        symbol.TryGetProperty(facet, out var value) ? value.ToString() : null;

    private static string[] ChoiceValues(JsonElement symbol) =>
        symbol.TryGetProperty("choices", out var choices)
            ? [.. choices.EnumerateArray().Select(choice => choice.GetProperty("choice").GetString()!)]
            : [];

    private static JsonNode? StarterModifiers(JsonElement manifest, string startersSource, string starter) =>
        JsonNode.Parse(manifest.GetProperty("sources").EnumerateArray()
            .Single(source => Text(source, "source") == $"{startersSource}{starter}/")
            .GetProperty("modifiers").GetRawText());

    private static bool ChoosesTheStarter(JsonElement symbol, string facet) =>
        Facet(symbol, facet)?.Contains("framework == ", StringComparison.Ordinal) == true;

    private static string PresentationDescriptor(string presentation) =>
        Path.Combine(StartersFolder, presentation, "Src", "Presentation", "AppTemplate.PresentationApp", "scaffold.json");

    private const string _workerStarter = "worker";

    private const string _coreSuffix = ".Core";

    private const string _desktopProject = "AppTemplate.PresentationApp";

    private static readonly string[] _desktopSessionCode =
    [
        "Src/Presentation/AppTemplate.PresentationApp/Common/Sessions",
        "Src/Presentation/AppTemplate.PresentationApp/Common/Hosting/SignInOptions.cs",
        "Src/Presentation/AppTemplate.PresentationApp/Common/Hosting/SignInOptionsValidator.cs",
        "Tests/Presentation/AppTemplate.PresentationApp.UnitTests/Common/Sessions",
        "Tests/Presentation/AppTemplate.PresentationApp.UnitTests/Common/Hosting/SignInOptionsValidatorTests.cs",
        "Tests/Presentation/AppTemplate.PresentationApp.UnitTests/TestSupport/ScriptedBrowser.cs",
        "Tests/Presentation/AppTemplate.PresentationApp.UnitTests/TestSupport/StubProvider.cs",
    ];

    private static string[] DesktopSessionCode(string starter)
    {
        string root = Path.Combine(StartersFolder, starter);
        string project = starter == "wpf" ? _desktopProject + _coreSuffix : _desktopProject;

        return [.. _desktopSessionCode
            .Select(path => Path.Combine(root, path.Replace(_desktopProject, project, StringComparison.Ordinal)))
            .SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : File.Exists(path) ? [path] : [])
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static readonly Regex _combination = new(@"\([^()]*&&[^()]*\)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _declaredNamespace = new(@"^namespace AppTemplate\.(\S+);$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static IEnumerable<JsonElement> MovedSources(List<JsonElement> sources) =>
        sources.Where(source => Text(source, "condition") == "(jobsWorker)"
            && Text(source, "target") is { } target && target.Contains("AppTemplate.Worker", StringComparison.Ordinal));

    private static IEnumerable<string> MovedFiles(JsonElement source)
    {
        string root = Path.Combine(ProjectReferenceGraph.RepositoryRoot, Text(source, "source")![2..]);

        return source.GetProperty("include").EnumerateArray()
            .Select(include => include.GetString()!.Replace("/**", string.Empty, StringComparison.Ordinal))
            .SelectMany(folder => folder.Contains('*', StringComparison.Ordinal)
                ? Directory.GetDirectories(Path.Combine(root, folder.Split("/*/")[0]))
                    .Select(feature => Path.Combine(feature, folder.Split("/*/")[1]))
                : [Path.Combine(root, folder)])
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories));
    }

    private static string[] WebStarters() =>
        [.. Directory.GetDirectories(StartersFolder)
            .Where(folder => File.Exists(Path.Combine(folder, "FrameworkSite", "scaffold.json")))
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    private static string[] PresentationStarters() =>
        [.. Directory.GetDirectories(StartersFolder)
            .Select(Path.GetFileName).OfType<string>()
            .Where(folder => File.Exists(PresentationDescriptor(folder)))
            .Order(StringComparer.Ordinal)];

    private static List<string> ChosenValues(string expression, string option) =>
        [.. Regex.Matches(expression, $@"\b{option} == ""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(match => match.Groups[1].Value)];

    private static bool Reaches(Dictionary<string, string> computed, string from, string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);

        while (pending.TryPop(out string? symbol))
        {
            if (symbol == target)
            {
                return true;
            }

            if (!seen.Add(symbol) || !computed.TryGetValue(symbol, out string? expression))
            {
                continue;
            }

            foreach (Match name in Regex.Matches(expression, @"\b[A-Za-z]\w*\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                pending.Push(name.Value);
            }
        }

        return false;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static bool IsRefusal(JsonElement symbol) =>
        symbol.TryGetProperty("choices", out var choices)
        && choices.EnumerateArray().All(choice => choice.GetProperty("choice").GetString() == "refused");

    private static List<string> UnconditionalExcludes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        return [.. manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray()
            .Where(modifier => !modifier.TryGetProperty("condition", out _))
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!))];
    }

    private static List<string> PackageExcludes()
    {
        string project = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "TemplatePackage.csproj");

        return [.. XDocument.Load(project)
            .Descendants("TemplateExclude")
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(pattern => pattern.Replace("$(TemplateRoot)", string.Empty, StringComparison.Ordinal))];
    }

    private static List<string> ConditionedBlocks(string[] lines, string symbol)
    {
        string opening = "#" + $"if ({symbol})";
        string closing = "#" + "endif";
        var blocks = new List<string>();
        List<string>? block = null;

        foreach (string line in lines)
        {
            if (line.Trim() == opening)
            {
                block = [];
            }
            else if (block is not null && line.Trim() == closing)
            {
                blocks.Add(string.Join('\n', block).TrimEnd());
                block = null;
            }
            else
            {
                block?.Add(line);
            }
        }

        return blocks;
    }

    private static List<string> FencedBlocksUnder(string[] lines, string bullet)
    {
        int start = Array.FindIndex(lines, line => line.StartsWith(bullet, StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, $"Src/Web/README.md has no bullet starting '{bullet}'.");

        var blocks = new List<string>();
        List<string>? block = null;
        int indent = 0;

        foreach (string line in lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || char.IsWhiteSpace(line[0])))
        {
            string trimmed = line.TrimStart();
            if (block is null && trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                block = [];
                indent = line.Length - trimmed.Length;
            }
            else if (block is not null && trimmed == "```")
            {
                blocks.Add(string.Join('\n', block).TrimEnd());
                block = null;
            }
            else
            {
                block?.Add(line.Length >= indent ? line[indent..] : line.TrimStart());
            }
        }

        return blocks;
    }

    private static string ManifestPath()
    {
        string manifest = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "template.json");

        File.Exists(manifest).ShouldBeTrue(
            $"'{manifest}' does not exist, so this repository packages a template `dotnet new` cannot read.");

        return manifest;
    }

    private static HashSet<string> DeclaredGuids()
    {
        string manifest = ManifestPath();

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));

        return [.. document.RootElement
            .GetProperty("guids")
            .EnumerateArray()
            .Select(element => element.GetString()!)];
    }

    private static HashSet<string> SolutionGuids()
    {
        string solution = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "AppTemplate.sln");

        File.Exists(solution).ShouldBeTrue($"The solution was not found at '{solution}'.");

        return [.. _guid
            .Matches(File.ReadAllText(solution))
            .Select(match => match.Value)];
    }
}

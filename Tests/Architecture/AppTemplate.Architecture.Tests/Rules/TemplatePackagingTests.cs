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

    [Fact]
    public void TheToolManifestWithoutSonar_IsTheToolManifestMinusTheScanner()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var full = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json")))!;
        var withoutSonar = JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, ".template.config", "variants", "no-sonar", ".config", "dotnet-tools.json")))!;

        full["tools"]!.AsObject().Remove("dotnet-sonarscanner").ShouldBeTrue("The tool manifest no longer declares the scanner.");

        JsonNode.DeepEquals(full, withoutSonar).ShouldBeTrue(
            ".template.config/variants/no-sonar/.config/dotnet-tools.json has drifted from .config/dotnet-tools.json.");
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
    [InlineData(".template.config")]
    [InlineData(".template.config/webapp/.template.config")]
    [InlineData(".template.config/webpackage/.template.config")]
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
        bool inRefusedOptions = symbols.GetProperty("refusedOptions").GetProperty("value").GetString()!
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
        string starters = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "starters");
        string[] folders = [.. Directory.GetDirectories(starters).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string[] frameworks = [.. symbols.GetProperty(option).GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString()!)
            .Where(choice => choice is not "none" and not "placeholder")
            .Order(StringComparer.Ordinal)];
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        folders.ShouldBe(frameworks, $"Each --{option} value with a CLI has its folder under .template.config/starters, named after it, and no other folder is there.");

        foreach (string framework in frameworks)
        {
            File.Exists(Path.Combine(starters, framework, "FrameworkSite", "scaffold.json")).ShouldBeTrue(
                $".template.config/starters/{framework}/FrameworkSite holds no scaffold.json, so web-scaffold would find nothing to run.");
            string chosenBy = computed.Single(symbol => symbol.Value == $"({option} == \"{framework}\")").Key;
            sources.ShouldContain(
                source => Text(source, "source") == $"{startersSource}{framework}/"
                    && Text(source, "target") == "./Src/Web/apps/"
                    && Text(source, "condition") == $"({chosenBy})",
                $"{folder}/template.json must copy {startersSource}{framework}/ into ./Src/Web/apps/ under the condition ({chosenBy}).");
            computed["webFramework"].ShouldContain(chosenBy, Case.Sensitive, $"webFramework in {folder}/template.json must name {chosenBy}, so the shared wiring comes with the {framework} starter.");
        }
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

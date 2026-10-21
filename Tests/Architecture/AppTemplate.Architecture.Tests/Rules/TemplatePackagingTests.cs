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

    private static bool IsRefusal(JsonElement symbol) =>
        symbol.TryGetProperty("choices", out var choices)
        && choices.EnumerateArray().All(choice => choice.GetProperty("choice").GetString() == "refused");

    private static List<string> PackageExcludes()
    {
        string project = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "TemplatePackage.csproj");

        return [.. XDocument.Load(project)
            .Descendants("TemplateExclude")
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(pattern => pattern.Replace("$(TemplateRoot)", string.Empty, StringComparison.Ordinal))];
    }

    [Theory]
    [InlineData("AGENTS.md", 150)]
    [InlineData("Src/Web/AGENTS.md", 60)]
    public void AnAgentFile_StaysABrief(string agentFile, int maximumLines)
    {
        string path = Path.Combine(ProjectReferenceGraph.RepositoryRoot, agentFile);

        File.Exists(path).ShouldBeTrue($"{agentFile} was not found.");

        File.ReadAllLines(path).Length.ShouldBeLessThanOrEqualTo(
            maximumLines,
            $"{agentFile} is the brief read before every change; here it holds every branch of every option. " +
            "Move what explains how something works to the page that owns it, and keep a link.");
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

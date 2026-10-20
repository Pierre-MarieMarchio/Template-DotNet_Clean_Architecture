using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What the <c>dotnet new</c> manifest has to keep in step with the solution.
/// <para>
/// The guid list is maintained by hand, and nothing about adding a project prompts anyone to
/// update it. A project missing from it leaves generated projects sharing an identifier that is
/// supposed to be unique per generation — a collision that surfaces far from its cause, in a NuGet
/// cache key or two solutions opened side by side.
/// </para>
/// <para>
/// These rules belong to the template repository: <c>.template.config/</c> is the template's own
/// metadata, so this file is not part of a generated project.
/// </para>
/// </summary>
public sealed class TemplatePackagingTests
{
    /// <summary>
    /// Visual Studio's own kind identifiers: a C# project and a solution folder. They name what a
    /// project *is*, so regenerating them would describe a kind that does not exist.
    /// </summary>
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

    /// <summary>
    /// The converse: a guid listed for regeneration that no longer exists is dead weight, and it
    /// hides whether the list is being maintained at all.
    /// </summary>
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

    /// <summary>
    /// The two type guids must stay out of the list, or every generated project would claim to hold
    /// a project kind Visual Studio has never heard of.
    /// </summary>
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

    /// <summary>
    /// <c>--files false</c> removes the storage projects from the generated solution through
    /// conditional lines on <c>files</c> in <c>AppTemplate.sln</c>. An IDE that rewrites the solution drops
    /// those lines, and the generated solution would then name projects that were not generated.
    /// </summary>
    [Fact]
    public void EveryStorageProjectLine_InTheSolution_IsConditionedOnFiles() =>
        // Spelled in two halves: written whole, the template engine would read the literal as a
        // condition of its own when it generates this file.
        AssertEveryProjectLineIsConditioned(".Storage", "#" + "if (files)", expectedProjects: 2);

    /// <summary>
    /// <c>--auth none</c> removes the supplied Auth projects the same way, through conditional lines
    /// on <c>localAuth</c>: the two modules, their two unit-test projects and the integration-test
    /// project of the identity store.
    /// </summary>
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

    /// <summary>
    /// <c>--sonar false</c> generates the tool manifest from a copy under <c>.template.config/</c>,
    /// because a tool manifest cannot hold a comment. The copy is the manifest minus the scanner,
    /// and a version bumped in one and not the other would make the two generations disagree.
    /// </summary>
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

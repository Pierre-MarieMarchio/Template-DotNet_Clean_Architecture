using System.Text.Json;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What the <c>dotnet new</c> manifest has to keep in step with the solution.
/// <para>
/// The guid list is maintained by hand, and nothing about adding a project prompts anyone to
/// update it. It has fallen behind twice, each time leaving generated projects sharing identifiers
/// that are supposed to be unique per generation — a collision that surfaces far from its cause, in
/// a NuGet cache key or two solutions opened side by side.
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

    private static HashSet<string> DeclaredGuids()
    {
        string manifest = Path.Combine(
            ProjectReferenceGraph.RepositoryRoot,
            ".template.config",
            "template.json");

        File.Exists(manifest).ShouldBeTrue($"The template manifest was not found at '{manifest}'.");

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

using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// That the generic HTTP code under <c>AppTemplate.Api/Common</c> knows no feature. It shares an
/// assembly with the feature controllers, so the compiler no longer says so: a <c>using</c> of a
/// feature namespace from the pipeline, the problem-details mapping or the controller base compiles
/// the same as any other, and it is the first step to a pipeline that has to change whenever a
/// feature does.
/// <para>
/// Read from source, as <see cref="HttpSurfaceTests"/> reads it: this test project deliberately
/// references no presentation project, and a using directive is a statement about the text.
/// </para>
/// </summary>
public sealed partial class HttpCommonBoundaryTests
{
    /// <summary>
    /// The feature namespaces of the layers the API sits on, which the common code may no more name
    /// than its own. What <c>Common/</c> does name from the layers below is their cross-cutting
    /// half — the application's results and ports, the domain's primitives — and the infrastructure
    /// modules the two host-specific files compose.
    /// </summary>
    private static readonly string[] _featureNamespaces =
    [
        $"{ArchitectureAssemblies.PresentationNamespace}.Features",
        ArchitectureAssemblies.ApplicationFeaturesNamespace,
        $"{ArchitectureAssemblies.ApplicationAuthNamespace}.Features",
        ArchitectureAssemblies.DomainFeaturesNamespace,
    ];

    /// <summary>
    /// A reference to <c>Features.Something</c> that names no namespace in front of it. Inside
    /// <c>namespace AppTemplate.Api.Common...</c> that spelling resolves to
    /// <c>AppTemplate.Api.Features</c>, so a rule that only looked for the full name would let it
    /// through. The look-behind is what keeps <c>httpContext.Features.Get</c> out of it.
    /// </summary>
    [GeneratedRegex(@"(?<![\w.])Features\.[A-Z]", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeFeatureReference();

    [Fact]
    public void TheCommonHttpCode_NamesNoFeature()
    {
        var files = CommonSourceFiles();

        var offences = files
            .SelectMany(file => Offences(file))
            .Order(StringComparer.Ordinal)
            .ToList();

        offences.ShouldBeEmpty(
            "Code under AppTemplate.Api/Common refers to a feature. Common holds the HTTP mechanisms "
            + "every feature shares — the controller base, problem details, preconditions, "
            + "idempotency, the pipeline — and they have to work for a feature that does not exist "
            + "yet. Invert the dependency: let the feature pass in what differs, or move the code "
            + "into the feature that owns it.");
    }

    private static List<string> CommonSourceFiles()
    {
        string common = Path.Combine(
            ProjectReferenceGraph.RootOf(ProjectReferenceGraph.Project("AppTemplate.Api")),
            "Common");

        Directory.Exists(common).ShouldBeTrue($"'{common}' was not found, so there is no common code to hold.");

        var files = Directory
            .EnumerateFiles(common, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

        // A path that stopped matching would leave this rule about nothing, and it would pass.
        files.Count.ShouldBeGreaterThanOrEqualTo(
            40,
            $"Only {files.Count} source files were found under '{common}', far fewer than the "
            + "common HTTP code holds, so the walk is not reading what it is meant to describe.");

        return files;
    }

    private static IEnumerable<string> Offences(string file)
    {
        string text = File.ReadAllText(file);
        string relative = Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file);

        foreach (string featureNamespace in _featureNamespaces)
        {
            if (text.Contains(featureNamespace + ".", StringComparison.Ordinal)
                || text.Contains(featureNamespace + ";", StringComparison.Ordinal))
            {
                yield return $"{relative}: names '{featureNamespace}'";
            }
        }

        if (RelativeFeatureReference().IsMatch(text))
        {
            yield return $"{relative}: names a 'Features.' namespace relative to its own";
        }
    }
}

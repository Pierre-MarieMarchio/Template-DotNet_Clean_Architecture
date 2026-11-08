using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed partial class HttpCommonBoundaryTests
{
    private static readonly string[] _featureNamespaces =
    [
        $"{ArchitectureAssemblies.PresentationNamespace}.Features",
        ArchitectureAssemblies.ApplicationFeaturesNamespace,
        //#if (localAuth)
        $"{ArchitectureAssemblies.ApplicationAuthNamespace}.Features",
        //#endif
        ArchitectureAssemblies.DomainFeaturesNamespace,
    ];

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
            "Code under AT.Api/Common refers to a feature. Common holds the HTTP mechanisms "
            + "every feature shares — the controller base, problem details, preconditions, "
            + "idempotency, the pipeline — and they have to work for a feature that does not exist "
            + "yet. Invert the dependency: let the feature pass in what differs, or move the code "
            + "into the feature that owns it. Remove the namespace or 'Features.' reference named in "
            + "each line above.");
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

        files.Count.ShouldBeGreaterThanOrEqualTo(
            40,
            $"Only {files.Count} source files were found under '{common}', far fewer than the "
            + "common HTTP code holds, so the walk is not reading what it is meant to describe.");

        return files;
    }

    private static IEnumerable<string> Offences(string file) =>
        OffencesIn(
            Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file),
            File.ReadAllText(file));

    private static IEnumerable<string> OffencesIn(string relative, string text)
    {
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

    [Fact]
    public void TheRule_IsSensitive_AndSeesAFeatureNamespaceAndARelativeFeatureReference()
    {
        string feature = _featureNamespaces[0];

        OffencesIn("a.cs", $"using {feature}.Todo;").ShouldBe([$"a.cs: names '{feature}'"]);
        OffencesIn("a.cs", $"namespace {feature};").ShouldBe([$"a.cs: names '{feature}'"]);
        OffencesIn("a.cs", "var x = Features.Todo.Create();").ShouldBe(["a.cs: names a 'Features.' namespace relative to its own"]);

        OffencesIn("a.cs", "using Microsoft.Extensions.Features.Collection;").ShouldBeEmpty();
        OffencesIn("a.cs", "var x = MyFeatures.Todo;").ShouldBeEmpty();
        OffencesIn("a.cs", "using System.Linq;").ShouldBeEmpty();
    }
}

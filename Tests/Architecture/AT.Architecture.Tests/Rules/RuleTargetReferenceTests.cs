using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class RuleTargetReferenceTests
{
    private static readonly string[] _loadedLayers = ["Domain", "Application", "Infrastructure"];

    [Fact]
    public void EveryProjectTheLayerRulesLoad_IsReferencedDirectly()
    {
        var loaded = _loadedLayers
            .SelectMany(ProjectReferenceGraph.ProjectsInLayer)
            .Select(project => project.Name)
            .ToList();

        loaded.ShouldNotBeEmpty("No project was found in the layers the rules load, so this rule reads nothing.");

        loaded
            .Where(name => !ProjectReferenceGraph.ArchitectureTestReferences.Contains(name))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "The rules load every project of these layers by name. One the architecture test project " +
                "reaches only through another project's reference loads while that reference lasts, and " +
                "its rules stop with a load error once it goes. Add a ProjectReference to each project " +
                $"named here in '{Path.GetFileName(ProjectReferenceGraph.ArchitectureTestProjectFile)}'.");
    }

    [Fact]
    public void TheReferenceGuard_IsSensitive_AndRefusesAProjectReachedOnlyThroughAnother()
    {
        ProjectReferenceGraph.ClosureOf(ProjectReferenceGraph.Project(ArchitectureAssemblies.DomainNamespace))
            .Select(project => project.Name)
            .ShouldContain(
                ArchitectureAssemblies.DomainCoreNamespace,
                "The counter-example needs a project the domain reaches.");

        var referenced = new HashSet<string>(StringComparer.Ordinal) { ArchitectureAssemblies.DomainNamespace };

        var refusal = Should.Throw<InvalidOperationException>(
            () => ArchitectureAssemblies.RequireDirectReference(ArchitectureAssemblies.DomainCoreNamespace, referenced));

        refusal.Message.ShouldContain($"'{ArchitectureAssemblies.DomainCoreNamespace}' is not referenced");
        refusal.Message.ShouldContain("<ProjectReference Include=\"..");
        Should.NotThrow(() => ArchitectureAssemblies.RequireDirectReference(ArchitectureAssemblies.DomainNamespace, referenced));
    }
}

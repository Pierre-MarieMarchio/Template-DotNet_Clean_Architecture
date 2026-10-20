using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class PackageBoundaryTests
{
    [Fact]
    public void EverySdkProject_ReferencesOnlySdkProjects()
    {
        var sdkProjects = ProjectReferenceGraph.SdkProjects.ToList();

        if (sdkProjects.Count == 0)
        {
            Assert.Skip("No project under Src/ declares IsSdkProject, so there is no SDK project to hold to this rule.");
        }

        var offences = sdkProjects
            .SelectMany(project => project.References
                .Where(reference => !ProjectReferenceGraph.IsSdkProject(reference))
                .Select(reference => $"{project.RelativePath} -> {reference} ({Describe(reference)})"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offences.ShouldBeEmpty(
            "An SDK project references a project that is not one. These projects are "
            + "vendored into a derived repository by copy, so they may reference each other and "
            + "nothing else — never a business project, never an infrastructure module, never a "
            + "host. A reference outward is what makes one un-vendorable: the copy drags in "
            + "the very application the project is supposed to know nothing about. Invert the "
            + "dependency behind a port, or move what is shared down into the SDK.");
    }

    private static string Describe(string reference)
    {
        if (ProjectReferenceGraph.IsInfrastructureModule(reference))
        {
            return "an infrastructure module";
        }

        if (!ProjectReferenceGraph.SourceProjects.TryGetValue(reference, out var project))
        {
            return "not a project under Src/";
        }

        return ProjectReferenceGraph.IsHost(project) ? "a host" : "a business project";
    }
}

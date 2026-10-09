using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What a project has to satisfy in order to be one of the vendored SDK projects, recognised by the
/// <c>IsSdkProject</c> it declares.
/// <para>
/// These projects are self-contained, ignorant of the application that consumes them, and consumed
/// by <c>ProjectReference</c> after being copied into a derived repository. The rules below are
/// what makes that true rather than aspirational.
/// </para>
/// <para>
/// <b>The rules only have a subject once an SDK project exists.</b> Until one declares
/// <c>IsSdkProject</c>, there is nothing to check, so the rules report themselves <em>skipped</em>
/// rather than passing: a rule that quietly succeeds where it checked nothing is the failure mode
/// this whole project exists to prevent, and a skip is visible in the run summary where a pass is
/// not.
/// </para>
/// </summary>
public sealed class PackageBoundaryTests
{
    /// <summary>
    /// The reference graph, not the IL: a reference nothing consumes yet is invisible to a
    /// type-level rule, and is exactly how an outward arrow gets established one release before
    /// anybody depends on it.
    /// </summary>
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

    /// <summary>
    /// How a forbidden reference reads in the failure message, so the fix is visible from it.
    /// </summary>
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

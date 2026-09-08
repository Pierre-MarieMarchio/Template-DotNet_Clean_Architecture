using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What a project has to satisfy in order to be one of the vendored SDK projects, recognised by the
/// <c>IsPackable</c> it declares.
/// <para>
/// These projects are written as if they were NuGet packages: self-contained, ignorant of the
/// application that consumes them, and consumed by <c>ProjectReference</c> after being copied into
/// a derived repository. Both properties below are what makes that true rather than aspirational —
/// an outward reference is what makes such a project unpackable and un-vendorable, and the
/// packaging metadata is what <c>dotnet pack</c> reads.
/// </para>
/// <para>
/// <b>Both rules only have a subject once an SDK project exists.</b> Until one declares
/// <c>IsPackable</c>, there is nothing to check, so the rules report themselves <em>skipped</em>
/// rather than passing: a rule that quietly succeeds where it checked nothing is the failure mode
/// this whole project exists to prevent, and a skip is visible in the run summary where a pass is
/// not.
/// </para>
/// </summary>
public sealed class PackageBoundaryTests
{
    /// <summary>
    /// What <c>dotnet pack</c> needs in order to produce a package a reader can identify: which
    /// package this is, what it is for, and who stands behind it.
    /// </summary>
    private static readonly string[] _requiredProperties = ["PackageId", "Description", "Authors"];

    /// <summary>
    /// A version would promise a published lineage that does not exist. These projects are
    /// vendored: a derived repository copies them and is free to tune them, so the copy's history
    /// is its own and no number of ours describes it.
    /// </summary>
    private static readonly string[] _forbiddenProperties = ["Version", "VersionPrefix"];

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
            Assert.Skip("No project under Src/ declares IsPackable, so there is no SDK project to hold to this rule.");
        }

        var offences = sdkProjects
            .SelectMany(project => project.References
                .Where(reference => !ProjectReferenceGraph.IsSdkProject(reference))
                .Select(reference => $"{project.RelativePath} -> {reference} ({Describe(reference)})"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offences.ShouldBeEmpty(
            "An SDK project references a project that is not one. These projects are written as if "
            + "they were NuGet packages and are vendored into a derived repository by copy, so they "
            + "may reference each other and nothing else — never a business project, never an "
            + "infrastructure module, never a host. A reference outward is what makes one "
            + "unpackable and un-vendorable: `dotnet pack` cannot express it, and the copy drags in "
            + "the very application the project is supposed to know nothing about. Invert the "
            + "dependency behind a port, or move what is shared down into the SDK.");
    }

    /// <summary>
    /// The metadata convention, read from the project file rather than from a build: the
    /// declaration is what CI packs, and what a reviewer of the diff sees.
    /// </summary>
    [Fact]
    public void EveryPackableProject_DeclaresItsPackageIdentity_AndNoVersion()
    {
        var packable = ProjectReferenceGraph.SdkProjects.ToList();

        if (packable.Count == 0)
        {
            Assert.Skip("No project under Src/ declares IsPackable, so there is no packaging metadata to check.");
        }

        var offences = new List<string>();

        foreach (var project in packable)
        {
            var properties = DeclaredProperties(project.RelativePath);

            offences.AddRange(_requiredProperties
                .Where(property => !properties.Contains(property))
                .Select(property => $"{project.RelativePath}: missing <{property}>"));

            offences.AddRange(_forbiddenProperties
                .Where(properties.Contains)
                .Select(property => $"{project.RelativePath}: declares <{property}>"));
        }

        offences
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A project that declares IsPackable must also declare PackageId, Description and "
                + "Authors — without them the produced package cannot be identified — and must "
                + "declare neither Version nor VersionPrefix, because these projects are vendored "
                + "rather than published and a version would promise a lineage that does not "
                + "exist.");
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

    /// <summary>
    /// The packages whose own nuspec carries the ASP.NET framework reference, so that referencing
    /// one pulls the shared framework in through the back door rather than through a
    /// <c>FrameworkReference</c> a reader can see.
    /// </summary>
    /// <remarks>
    /// <c>Asp.Versioning.Mvc</c> does it through its dependency <c>Asp.Versioning.Http</c>, whose
    /// own nuspec is clean — which is what makes that one impossible to spot by reading the package
    /// name. This list is why the rule below can be stated over the project files instead of over a
    /// restore graph.
    /// </remarks>
    private static readonly string[] _packagesCarryingTheFramework =
    [
        "Asp.Versioning.Mvc",
        "Asp.Versioning.Mvc.ApiExplorer",
        "Microsoft.AspNetCore.OpenApi",
        "OpenTelemetry.Instrumentation.AspNetCore",
        "Scalar.AspNetCore",
    ];

    /// <summary>
    /// An SDK project that does not declare the ASP.NET framework reference may not acquire it
    /// through a package either.
    /// </summary>
    /// <remarks>
    /// This is the one property the presentation half of the SDK is split in two for. A host that
    /// is not an HTTP server — a desktop front end, a CLI — has to be able to take the clock, the
    /// culture, the outbound budget and the application layer without inheriting ASP.NET, and the
    /// only thing standing between that promise and a package reference added in good faith is
    /// this rule. It was a comment until now, and a comment is not a check.
    /// <para>
    /// Deliberately stated without naming a project. The subject is "an SDK project that carries
    /// the framework" and "one that does not", read off the project files, so a sixth SDK project
    /// is governed the day it exists rather than the day someone remembers a list — the failure
    /// mode decision 15 was written about.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnSdkProjectWithoutTheAspNetFramework_CannotInheritItFromAPackage()
    {
        var sdkProjects = ProjectReferenceGraph.SdkProjects.ToList();

        if (sdkProjects.Count == 0)
        {
            Assert.Skip("No project under Src/ declares IsPackable, so there is no SDK boundary to check.");
        }

        var carrying = sdkProjects
            .Where(project => DeclaredItems(project.RelativePath, "FrameworkReference")
                .Contains("Microsoft.AspNetCore.App"))
            .ToList();

        var notCarrying = sdkProjects.Except(carrying).ToList();

        // Both halves have to exist, or the rule is about one kind of project and silently says
        // nothing about the other. The split of the presentation layer in two is exactly the claim
        // that both kinds exist.
        carrying.ShouldNotBeEmpty(
            "No SDK project declares the ASP.NET framework reference, so the HTTP half of the SDK "
            + "either does not exist or acquires the framework some other way — and the rule below "
            + "would then be checking every SDK project against a boundary none of them is on.");

        notCarrying.ShouldNotBeEmpty(
            "Every SDK project declares the ASP.NET framework reference, so nothing is left that a "
            + "non-HTTP host could take. That is the property the presentation layer is split in "
            + "two for, and this rule is what holds it.");

        var offences = new List<string>();

        foreach (var project in notCarrying)
        {
            var packages = DeclaredItems(project.RelativePath, "PackageReference");

            offences.AddRange(_packagesCarryingTheFramework
                .Where(packages.Contains)
                .Select(package =>
                    $"{project.RelativePath}: references '{package}', whose own nuspec carries the "
                    + "ASP.NET framework"));
        }

        offences
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "An SDK project that declares no FrameworkReference must not reference a package "
                + "that carries one, because the result is the same shared framework with nothing "
                + "in the project file saying so. A host without an HTTP surface would inherit "
                + "ASP.NET from a project that exists precisely so it would not have to.");
    }

    /// <summary>
    /// The <c>Include</c> values of one item type a project file states, unqualified for the same
    /// reason as <see cref="DeclaredProperties"/>: an SDK-style project file carries no XML
    /// namespace.
    /// </summary>
    private static HashSet<string> DeclaredItems(string relativePath, string itemName)
    {
        string projectFile = Path.Combine(ProjectReferenceGraph.RepositoryRoot, relativePath);

        File.Exists(projectFile).ShouldBeTrue($"The project file was not found at '{projectFile}'.");

        return [.. XDocument
            .Load(projectFile)
            .Descendants(itemName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrEmpty(include))
            .Select(include => include!)];
    }

    /// <summary>
    /// The MSBuild property names a project file states. SDK-style project files carry no XML
    /// namespace, so the elements are matched unqualified; only children of a
    /// <c>PropertyGroup</c> count, so an item's metadata is not mistaken for a property.
    /// </summary>
    private static HashSet<string> DeclaredProperties(string relativePath)
    {
        string projectFile = Path.Combine(ProjectReferenceGraph.RepositoryRoot, relativePath);

        File.Exists(projectFile).ShouldBeTrue($"The project file was not found at '{projectFile}'.");

        return [.. XDocument
            .Load(projectFile)
            .Descendants("PropertyGroup")
            .Elements()
            .Select(element => element.Name.LocalName)];
    }
}

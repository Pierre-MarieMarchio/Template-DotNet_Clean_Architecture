using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// That everything under the HTTP host can be taken by a host that is not one: a desktop front
/// end, a command line, a runner that only does background work. Domain, application, infrastructure and the presentation
/// foundation may not require the ASP.NET shared framework, whether they declare it, take it from
/// the Web SDK, or inherit it through a package or through a project they reference.
/// <para>
/// Read from the project files, through <c>ProjectReference</c> and not just the project's own
/// declarations, for the reason <see cref="PackageBoundaryTests"/> gives: a dependency nothing
/// consumes yet is invisible to a rule written over types, and the framework arrives one hop away
/// as easily as in the project itself. A comment saying a project is portable is not a check, and
/// this is the check.
/// </para>
/// </summary>
public sealed class PortabilityTests
{
    private const string _aspNetFramework = "Microsoft.AspNetCore.App";
    private const string _webSdk = "Microsoft.NET.Sdk.Web";
    private const string _presentationCoreProject = "AppTemplate.Presentation.Core";

    /// <summary>
    /// The layers that hold no HTTP code, named by the folder <c>Src</c> groups them under. The
    /// presentation layer is not here as a whole: it holds the HTTP host itself, so only its
    /// portable foundation is a subject.
    /// </summary>
    private static readonly string[] _portableLayers = ["Domain", "Application", "Infrastructure"];

    /// <summary>
    /// The projects that are allowed to require the ASP.NET shared framework, and why. Each one is
    /// an adapter whose job is to plug into the HTTP request pipeline, which cannot be done without
    /// it. An entry here is a decision, and it is checked below to still be true: an adapter that
    /// has stopped needing the framework has to leave this list rather than sit on it.
    /// </summary>
    private static readonly Dictionary<string, string> _httpSpecificAdapters =
        new(StringComparer.Ordinal)
        {
            ["AppTemplate.Infrastructure.Auth"] =
                "its Microsoft.AspNetCore.Authentication.JwtBearer package is the bearer-token handler "
                + "of the ASP.NET authentication pipeline, and carries the shared framework in its own "
                + "nuspec",
        };

    /// <summary>
    /// Packages that have ASP.NET Core in their name and none of it in their requirements: both
    /// target plain <c>net10.0</c> and declare no framework reference, which was read from their
    /// nuspec rather than assumed from the name. The identity stores and the key ring are storage,
    /// not HTTP, and the persistence modules use them without a request in sight.
    /// </summary>
    private static readonly HashSet<string> _namedAfterAspNetButFrameworkFree = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.DataProtection.EntityFrameworkCore",
        "Microsoft.AspNetCore.Identity.EntityFrameworkCore",
    };

    [Fact]
    public void ThePortableProjects_DoNotReachTheAspNetFramework()
    {
        var subjects = PortableProjects().ToList();

        subjects.ShouldNotBeEmpty(
            "No portable project was found, so this rule would pass having checked nothing.");

        var offences = subjects
            .SelectMany(subject => ProjectReferenceGraph.ClosureOf(subject)
                .SelectMany(project => AspNetDependencies(project)
                    .Select(dependency => Describe(subject, project, dependency))))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        offences.ShouldBeEmpty(
            "A project that is meant to run in any .NET host requires ASP.NET Core, directly or "
            + "through something it references. A host without an HTTP server would then need the "
            + "ASP.NET Core runtime installed to use it. Move the HTTP-specific part into "
            + "AppTemplate.Api, or, if the project really is an HTTP adapter, name it in "
            + "_httpSpecificAdapters with the reason.");
    }

    /// <summary>
    /// The vendored SDK projects carry no HTTP half any more, so every one of them is a subject of
    /// the rule above. Stated over what each declares, so an SDK project added later that lands
    /// outside the layers read there is caught here instead of escaping both rules.
    /// </summary>
    [Fact]
    public void EverySdkProject_IsHeldToThePortabilityRule()
    {
        var subjects = PortableProjects().Select(project => project.Name).ToHashSet(StringComparer.Ordinal);

        var unheld = ProjectReferenceGraph.SdkProjects
            .Select(project => project.Name)
            .Where(name => !subjects.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();

        unheld.ShouldBeEmpty(
            "An SDK project is not covered by the portability rule. The SDK exists to be taken by "
            + "any .NET host, so it may not require ASP.NET Core: add the project to the layers "
            + "this rule reads, or to the subjects it names.");
    }

    /// <summary>
    /// The exemptions are claims too. One that no longer holds — the adapter dropped its
    /// ASP.NET dependency, or the project was removed — would keep exempting a project that has
    /// become subject to the rule, and nothing would say so.
    /// </summary>
    [Fact]
    public void EveryHttpSpecificAdapter_StillRequiresTheAspNetFramework()
    {
        foreach ((string name, string reason) in _httpSpecificAdapters)
        {
            ProjectReferenceGraph.SourceProjects.ShouldContainKey(
                name,
                $"'{name}' is exempted as an HTTP adapter ({reason}) but is not a project under Src/.");

            var project = ProjectReferenceGraph.Project(name);

            AspNetDependencies(project).ShouldNotBeEmpty(
                $"'{name}' is exempted from the portability rule because {reason}, but it no longer "
                + "declares anything that requires ASP.NET Core. Remove it from "
                + "_httpSpecificAdapters so the rule holds it again.");
        }
    }

    /// <summary>
    /// No portable project may reach an exempt adapter either: the exemption covers the adapter,
    /// not whatever references it. Stated separately because the first rule would report it as the
    /// adapter's own dependency, which reads as if the adapter were the one at fault.
    /// </summary>
    [Fact]
    public void NoPortableProject_ReferencesAnHttpSpecificAdapter()
    {
        var offences = PortableProjects()
            .SelectMany(subject => ProjectReferenceGraph.ClosureOf(subject)
                .Where(project => project != subject && _httpSpecificAdapters.ContainsKey(project.Name))
                .Select(project => $"{subject.RelativePath} -> {project.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offences.ShouldBeEmpty(
            "A portable project references an HTTP-specific adapter, which makes it require ASP.NET "
            + "Core by the back door. Only the host composes an adapter; nothing portable may "
            + "depend on one.");
    }

    /// <summary>
    /// The subjects: every project of the portable layers that is not an exempt adapter, and the
    /// presentation layer's foundation. Population comes from the layer folders, so a project added
    /// to one is held to the rule the day it exists.
    /// </summary>
    private static IEnumerable<ProjectNode> PortableProjects() =>
        _portableLayers
            .SelectMany(ProjectReferenceGraph.ProjectsInLayer)
            .Where(project => !_httpSpecificAdapters.ContainsKey(project.Name))
            .Append(ProjectReferenceGraph.Project(_presentationCoreProject));

    /// <summary>
    /// What one project file, on its own, declares that requires ASP.NET Core. By name for the
    /// packages, because a package that carries the framework in its nuspec (the Asp.Versioning
    /// ones do it through a dependency whose own nuspec is clean) cannot be recognised from a
    /// restore graph that has not been restored, and a rule that needs one would not run on a
    /// fresh checkout.
    /// </summary>
    private static IEnumerable<string> AspNetDependencies(ProjectNode project)
    {
        if (project.Sdk.Equals(_webSdk, StringComparison.Ordinal))
        {
            yield return $"the {_webSdk} SDK";
        }

        if (project.FrameworkReferences.Contains(_aspNetFramework))
        {
            yield return $"a FrameworkReference to {_aspNetFramework}";
        }

        foreach (string package in project.Packages.Order(StringComparer.Ordinal))
        {
            if (IsAspNetPackage(package))
            {
                yield return $"the package {package}";
            }
        }
    }

    private static bool IsAspNetPackage(string package) =>
        !_namedAfterAspNetButFrameworkFree.Contains(package)
        && (package.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("Asp.Versioning", StringComparison.Ordinal));

    private static string Describe(ProjectNode subject, ProjectNode through, string dependency) =>
        through == subject
            ? $"{subject.RelativePath}: declares {dependency}"
            : $"{subject.RelativePath}: reaches {dependency} through {through.Name}";
}

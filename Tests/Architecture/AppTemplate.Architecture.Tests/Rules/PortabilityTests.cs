using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class PortabilityTests
{
    private const string _aspNetFramework = "Microsoft.AspNetCore.App";
    private const string _webSdk = "Microsoft.NET.Sdk.Web";
    private const string _presentationCoreProject = "AppTemplate.Presentation.Core";

    private static readonly string[] _portableLayers = ["Domain", "Application", "Infrastructure"];

    private static readonly Dictionary<string, string> _httpSpecificAdapters =
        new(StringComparer.Ordinal)
        {
            //#if (localAuth)
            ["AppTemplate.Infrastructure.Auth"] =
                "its Microsoft.AspNetCore.Authentication.JwtBearer package is the bearer-token handler "
                + "of the ASP.NET authentication pipeline, and carries the shared framework in its own "
                + "nuspec",
            //#endif
        };

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

    private static IEnumerable<ProjectNode> PortableProjects() =>
        _portableLayers
            .SelectMany(ProjectReferenceGraph.ProjectsInLayer)
            .Where(project => !_httpSpecificAdapters.ContainsKey(project.Name))
            .Append(ProjectReferenceGraph.Project(_presentationCoreProject));

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

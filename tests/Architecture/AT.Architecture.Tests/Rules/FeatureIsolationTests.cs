using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class FeatureIsolationTests
{
    private const string _applicationFeatures = "AppTemplate.Application.Features";
    private const string _persistenceFeatures = "AppTemplate.Infrastructure.Persistence.Features";
    private const string _domainFeatures = "AppTemplate.Domain.Features";

    private const string _portsSegment = "Ports";

    private const string _eventsSegment = "Events";

    [Fact]
    public void AFeatureOfTheApplicationProject_ReachesAnotherOnlyThroughItsPortsAndEvents()
    {
        var offenders = Violations(ArchitectureAssemblies.Application, _applicationFeatures);

        offenders.ShouldBeEmpty(Because("application"));
    }

    [Fact]
    public void AFeatureOfThePersistenceProject_ReachesAnotherOnlyThroughItsPortsAndEvents()
    {
        var offenders = Violations(ArchitectureAssemblies.Persistence, _persistenceFeatures);

        offenders.ShouldBeEmpty(Because("persistence"));
    }

    [Fact]
    public void EveryFeature_IsASubjectInBothProjects()
    {
        var domain = FeaturesIn(ArchitectureAssemblies.Domain, _domainFeatures);
        var application = FeaturesIn(ArchitectureAssemblies.Application, _applicationFeatures);
        var persistence = FeaturesIn(ArchitectureAssemblies.Persistence, _persistenceFeatures);

        //#if (aggregates)
        domain.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "Fewer features were found in the domain project than this template holds, so the rules "
            + "in this class would be read over a population that is not the real one.");
        //#endif

        application
            .Except(persistence, StringComparer.Ordinal)
            .ShouldBeEmpty("A feature has application code but no persistence folder, so it is not a subject.");

        persistence
            .Except(application, StringComparer.Ordinal)
            .ShouldBeEmpty("A feature has persistence code but no application folder, so it is not a subject.");

        domain
            .Except(application, StringComparer.Ordinal)
            .ShouldBeEmpty("A feature of the domain is absent from the application project, so it is not a subject.");
    }

    [Fact]
    public void AFeatureWithoutADomainFolder_IsAServiceOverAStore()
    {
        var withoutDomain = FeaturesIn(ArchitectureAssemblies.Application, _applicationFeatures)
            .Except(FeaturesIn(ArchitectureAssemblies.Domain, _domainFeatures), StringComparer.Ordinal);

        List<string> offenders = [];

        foreach (string feature in withoutDomain)
        {
            var types = ArchitectureAssemblies.Application.GetTypes()
                .Where(type => type.Namespace?.StartsWith($"{_applicationFeatures}.{feature}.", StringComparison.Ordinal) == true)
                .ToList();

            bool Holds(string segment) =>
                types.Any(type => type.Namespace!.Split('.').Contains(segment, StringComparer.Ordinal));

            if (!Holds("Services"))
            {
                offenders.Add($"{feature}: has no domain folder and no Services/, so nothing offers its operations");
            }

            if (Holds("UseCases"))
            {
                offenders.Add($"{feature}: has no domain folder and still declares use cases; one use case per operation is the rich shape");
            }

            if (!types.Any(type => type is { IsInterface: true, IsPublic: true }
                && (type.Name.EndsWith("Store", StringComparison.Ordinal)
                    || type.Name.EndsWith("Queries", StringComparison.Ordinal))
                && type.Namespace!.Split('.').Contains(_portsSegment, StringComparer.Ordinal)))
            {
                offenders.Add($"{feature}: has no domain folder and declares no Store or Queries port for its storage");
            }
        }

        offenders.ShouldBeEmpty(
            "A feature may omit its domain folder only while it is a service over its storage ports: no "
            + "rules protect a state, so there is no aggregate to hold and no use case to split the "
            + "service into.");
    }

    [Fact]
    public void TheRule_IsSensitive_AndDetectsAFeatureReachingIntoItsOwnNamespaces()
    {
        /*#if (anyFeature)*/
        string feature = FeaturesIn(ArchitectureAssemblies.Persistence, _persistenceFeatures).First();
        /*#else
        string? feature = FeaturesIn(ArchitectureAssemblies.Persistence, _persistenceFeatures).FirstOrDefault();

        if (feature is null)
        {
            return;
        }
        #endif*/
        string[] own = NamespacesOf(ArchitectureAssemblies.Persistence, $"{_persistenceFeatures}.{feature}")
            .ToArray();

        var subjects = Types.InAssembly(ArchitectureAssemblies.Persistence)
            .That()
            .ResideInNamespaceStartingWith($"{_persistenceFeatures}.{feature}.");

        RuleAssertions.RequireTypes(subjects, $"a type under '{_persistenceFeatures}.{feature}'");

        subjects
            .ShouldNot()
            .HaveDependencyOnAny(own)
            .GetResult()
            .ShouldDetectAViolation(
                $"The '{feature}' persistence types name their own records and configurations, so "
                + "forbidding those namespaces to them must fail. It did not, which means the "
                + "dependency matching is inert.");
    }

    private static List<string> Violations(Assembly assembly, string featuresNamespace)
    {
        RuleAssertions.RequireTypes(assembly);

        var features = FeaturesIn(assembly, featuresNamespace);

        //#if (todoLists || (files && notes))
        features.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            $"Fewer than two features were found under '{featuresNamespace}', so the rule would "
            + "pass over a population that is not the real one.");

        //#endif
        List<string> offenders = [];

        foreach (string subject in features)
        {
            var subjects = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceStartingWith($"{featuresNamespace}.{subject}.");

            RuleAssertions.RequireTypes(subjects, $"a type under '{featuresNamespace}.{subject}'");

            string[] forbidden = [.. features
                .Where(other => !string.Equals(other, subject, StringComparison.Ordinal))
                .SelectMany(ForeignInternals)];

            //#if (todoLists || (files && notes))
            forbidden.ShouldNotBeEmpty($"No forbidden namespaces were computed for '{subject}'.");

            //#endif
            var result = subjects
                .ShouldNot()
                .HaveDependencyOnAny(forbidden)
                .GetResult();

            offenders.AddRange((result.FailingTypeNames ?? [])
                .Select(type => $"{subject}: {type}"));
        }

        offenders.Sort(StringComparer.Ordinal);

        return offenders;
    }

    private static IEnumerable<string> ForeignInternals(string feature) =>
        NamespacesOf(ArchitectureAssemblies.Persistence, $"{_persistenceFeatures}.{feature}")
            .Concat(NamespacesOf(ArchitectureAssemblies.Application, $"{_applicationFeatures}.{feature}")
                .Where(ns => !IsSurface(ns, _portsSegment)))
            .Concat(NamespacesOf(ArchitectureAssemblies.Domain, $"{_domainFeatures}.{feature}")
                .Where(ns => !IsSurface(ns, _eventsSegment)));

    private static bool IsSurface(string ns, string segment) =>
        ns.Split('.').Contains(segment, StringComparer.Ordinal);

    private static List<string> FeaturesIn(Assembly assembly, string featuresNamespace)
    {
        string prefix = featuresNamespace + ".";

        return [.. NamespacesOf(assembly, featuresNamespace)
            .Select(ns => ns[prefix.Length..].Split('.')[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> NamespacesOf(Assembly assembly, string root) =>
        assembly.GetTypes()
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(ns => ns.StartsWith(root + ".", StringComparison.Ordinal)
                || string.Equals(ns, root, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

    private static string Because(string project) =>
        $"A type of one feature in the {project} project names a type of another feature that is not "
        + "that feature's public application port (Application/Features/<F>/Ports) or one of its "
        + "domain events (Domain/Features/<F>/Events). Features meet through those two surfaces and "
        + "nothing else — not through records, EF configurations, DbSets, DTOs, services or use "
        + "cases — so that any of them can be extracted without editing the others. Ask for what "
        + "is needed through a narrow port the owning feature declares.";
}

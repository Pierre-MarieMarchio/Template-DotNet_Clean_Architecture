using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The modular-monolith rule between features: a feature uses another feature only through that
/// feature's public application port or its domain events, never through its records, its EF
/// configurations, its <c>DbSet</c>s or any other type it keeps to itself.
/// <para>
/// What makes it a rule rather than a habit is extraction. Every feature must be able to leave the
/// shared projects later, and each reference that reaches past a port is a place where the feature
/// that stays would have to be edited to let it go. The rule is read over both projects that hold
/// feature code — application and persistence — because the second is where a query can read
/// another feature's rows without any type signature giving it away.
/// </para>
/// <para>
/// A feature's <b>surface</b> is two namespaces: <c>Ports</c> in the application project, and
/// <c>Events</c> in the domain project. Everything else of a feature is its own. If a legitimate
/// exception ever exists it is named in this class, with the reason, rather than excused by
/// narrowing a namespace until the offender falls outside it.
/// </para>
/// </summary>
public sealed class FeatureIsolationTests
{
    private const string _applicationFeatures = "AppTemplate.Application.Features";
    private const string _persistenceFeatures = "AppTemplate.Infrastructure.Persistence.Features";
    private const string _domainFeatures = "AppTemplate.Domain.Features";

    /// <summary>The application-layer segment a feature publishes to the others.</summary>
    private const string _portsSegment = "Ports";

    /// <summary>The domain-layer segment a feature publishes to the others.</summary>
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

    /// <summary>
    /// Every feature has to be a subject of the two rules above, or a feature that is not read is a
    /// feature that may reach anywhere while the suite reports success. The population is whatever
    /// the application project holds, and each of its features must be present in the persistence
    /// project too. A domain folder is not required of all of them, only of the rich shape: see
    /// <see cref="AFeatureWithoutADomainFolder_IsAServiceOverAStore"/>.
    /// </summary>
    [Fact]
    public void EveryFeature_IsASubjectInBothProjects()
    {
        var domain = FeaturesIn(ArchitectureAssemblies.Domain, _domainFeatures);
        var application = FeaturesIn(ArchitectureAssemblies.Application, _applicationFeatures);
        var persistence = FeaturesIn(ArchitectureAssemblies.Persistence, _persistenceFeatures);

        domain.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            "Fewer features were found in the domain project than this template holds, so the rules "
            + "in this class would be read over a population that is not the real one.");

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

    /// <summary>
    /// The one exemption from having a domain model, stated as a shape rather than as a list: a
    /// feature with no domain folder has no rules protecting a state, so what it offers is a service
    /// over its storage ports (a store to write, queries to read), one method per operation, and
    /// nothing that would only make sense with an aggregate — no use cases, no repository. A feature that grows a rule moves to the rich shape
    /// (<c>docs/ADDING-A-FEATURE.md</c>), and this rule stops applying to it.
    /// <para>
    /// Not floored on purpose: the example of this shape is removable, and a template without it has
    /// no feature this rule is about.
    /// </para>
    /// </summary>
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

            // A Store is the write port; a feature that only reads has just the Queries port, and a
            // service over that is still a service over storage with nothing to protect.
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

    /// <summary>
    /// Proves the rule bites. A feature's own persistence namespaces are, by definition, ones its
    /// types depend on; handing them to the rule as the forbidden set must therefore fail, or
    /// <c>HaveDependencyOnAny</c> would be matching nothing and the two rules above would be
    /// decorative.
    /// </summary>
    [Fact]
    public void TheRule_IsSensitive_AndDetectsAFeatureReachingIntoItsOwnNamespaces()
    {
        string feature = FeaturesIn(ArchitectureAssemblies.Persistence, _persistenceFeatures).First();
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

    /// <summary>
    /// The offending types, as <c>subject feature -> foreign feature: type</c>. Empty when every
    /// feature of the project keeps to the other features' surfaces.
    /// </summary>
    private static List<string> Violations(Assembly assembly, string featuresNamespace)
    {
        RuleAssertions.RequireTypes(assembly);

        var features = FeaturesIn(assembly, featuresNamespace);

        features.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            $"Fewer than three features were found under '{featuresNamespace}', so the rule would "
            + "pass over a population that is not the real one.");

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

            forbidden.ShouldNotBeEmpty($"No forbidden namespaces were computed for '{subject}'.");

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

    /// <summary>
    /// Everything of one feature that another feature may not name: all of its persistence, and, in
    /// the application and domain projects, all of it but the surface.
    /// </summary>
    private static IEnumerable<string> ForeignInternals(string feature) =>
        NamespacesOf(ArchitectureAssemblies.Persistence, $"{_persistenceFeatures}.{feature}")
            .Concat(NamespacesOf(ArchitectureAssemblies.Application, $"{_applicationFeatures}.{feature}")
                .Where(ns => !IsSurface(ns, _portsSegment)))
            .Concat(NamespacesOf(ArchitectureAssemblies.Domain, $"{_domainFeatures}.{feature}")
                .Where(ns => !IsSurface(ns, _eventsSegment)));

    /// <summary>Whether a namespace is, or sits below, the feature's published segment.</summary>
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

    /// <summary>
    /// Every distinct namespace at or below <paramref name="root"/> that the assembly declares a
    /// type in. Computed, not listed, so a folder added to a feature is covered on arrival.
    /// </summary>
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

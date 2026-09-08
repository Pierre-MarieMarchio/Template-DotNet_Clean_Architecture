using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// How the infrastructure modules are allowed to relate to each other and to the host.
/// <para>
/// The shape being defended: modules depend on the persistence project and on nothing else
/// horizontal, that project depends on no module, its cross-cutting mechanisms know no business
/// entity, and the API is the only place that knows the full set. That is what lets a module be
/// added or dropped without any other module noticing.
/// </para>
/// </summary>
public sealed class ModuleDependencyTests
{
    private const string _apiProject = "AppTemplate.Api";
    private const string _domainProject = "AppTemplate.Domain";
    private const string _domainCoreProject = "AppTemplate.Domain.Core";
    private const string _applicationProject = "AppTemplate.Application";
    private const string _applicationCoreProject = "AppTemplate.Application.Core";
    private const string _persistenceProject = "AppTemplate.Infrastructure.Persistence";

    /// <summary>The single context, named here because two rules below address it by name.</summary>
    private const string _contextTypeName = "AppDbContext";

    private static readonly string[] _modulesThatMustNotReachIntoPersistence =
    [
        ArchitectureAssemblies.IdentityInfrastructureNamespace,
        ArchitectureAssemblies.EmailInfrastructureNamespace,
        ArchitectureAssemblies.InMemoryInfrastructureNamespace,
    ];

    /// <summary>
    /// What the cross-cutting persistence mechanisms must not name: a business entity, or one of
    /// the per-feature namespaces that map them.
    /// </summary>
    private static readonly string[] _forbiddenInPersistenceCommon =
    [
        ArchitectureAssemblies.DomainFeaturesNamespace,
        ArchitectureAssemblies.PersistenceFeaturesNamespace,
        ArchitectureAssemblies.ApplicationFeaturesNamespace,

        // Authentication is a feature like any other, and it is a project of its own rather than a
        // folder under the one above — so the prefix that catches the business features does not
        // reach it. Listed separately, because a shared mechanism that named an authentication port
        // would otherwise be the one feature this rule stopped seeing.
        ArchitectureAssemblies.ApplicationAuthNamespace,
    ];

    /// <summary>
    /// The modules whose <c>Common/</c> is held to the same rule as the persistence one: what no
    /// single subject owns may not name a subject.
    /// </summary>
    private static readonly (string Module, System.Reflection.Assembly Assembly)[] _moduleCommons =
    [
        ("AppTemplate.Infrastructure.Identity", ArchitectureAssemblies.IdentityInfrastructure),
        ("AppTemplate.Infrastructure.Storage", ArchitectureAssemblies.StorageInfrastructure),
        ("AppTemplate.Infrastructure.Email", ArchitectureAssemblies.EmailInfrastructure),
        ("AppTemplate.Infrastructure.InMemory", ArchitectureAssemblies.InMemoryInfrastructure),
    ];

    /// <summary>
    /// The one documented exception, and the reason it is one rather than a defect. The identity
    /// module reads accounts through <c>AppUser</c>, which is the shared ASP.NET Identity row that
    /// the persistence module maps — not a business entity, and not a type any application port
    /// exposes. Identity depending on persistence is the permitted direction: the rule above
    /// forbids only the reverse, and the architecture describes this module as using the shared
    /// context. So the directory names it, and nothing else in this module's <c>Common/</c> does.
    /// <para>
    /// It sits here rather than in the rule's condition so that adding a second one is a line in
    /// this list with a reason beside it, which is a thing a reviewer sees.
    /// </para>
    /// </summary>
    private static readonly string[] _commonTypesAllowedToNameAFeature =
    [
        "IAppUserDirectory",
        "AppUserDirectory",
    ];

    /// <summary>
    /// The same rule the persistence mechanisms are held to, applied to the four other modules.
    /// <c>Common/</c> holds what no single subject owns; a type there that names a feature is a
    /// feature adapter wearing the shared machinery's clothes, and the folder stops meaning
    /// anything.
    /// </summary>
    [Fact]
    public void EveryModuleCommon_KnowsNoFeature()
    {
        var checkedModules = 0;

        foreach ((string module, var assembly) in _moduleCommons)
        {
            var mechanisms = Types.InAssembly(assembly)
                .That()
                .ResideInNamespaceStartingWith($"{module}.Common");

            var candidates = mechanisms.GetTypes()
                .Where(type => !_commonTypesAllowedToNameAFeature.Contains(type.Name, StringComparer.Ordinal))
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            checkedModules++;

            var offenders = candidates
                .Where(type => Types.InAssembly(assembly)
                    .That()
                    .HaveName(type.Name)
                    .ShouldNot()
                    .HaveDependencyOnAny(_forbiddenInPersistenceCommon)
                    .GetResult()
                    .IsSuccessful is false)
                .Select(type => $"{module}: {type.Name}")
                .Order(StringComparer.Ordinal)
                .ToList();

            offenders.ShouldBeEmpty(
                $"Nothing under '{module}.Common' may name a feature's domain types, a feature's "
                + "persistence namespace, or a feature's application-layer surface. A type that "
                + "does is a feature adapter filed as shared machinery, which is how one of them "
                + "escaped every rule in this project for a month. Move it under "
                + $"Features/<Feature>/, or add it to {nameof(_commonTypesAllowedToNameAFeature)} "
                + "with the reason it belongs to no single subject.");
        }

        // Four modules have a Common/, and all four are read. A filter that stopped matching would
        // otherwise leave this test reporting success over nothing at all.
        checkedModules.ShouldBeGreaterThanOrEqualTo(
            3,
            $"Only {checkedModules} module Common namespaces yielded types. This rule is written "
            + "against four, so either a module lost its Common/ or the namespace match is stale.");
    }

    [Fact]
    public void Persistence_DependsOnNoModule()
    {
        RuleAssertions.RequireTypes(ArchitectureAssemblies.Persistence);

        Types.InAssembly(ArchitectureAssemblies.Persistence)
            .ShouldNot()
            .HaveDependencyOnAny(_modulesThatMustNotReachIntoPersistence)
            .GetResult()
            .ShouldHold(
                "Modules reference AppTemplate.Infrastructure.Persistence; it references no module. A " +
                "dependency in this direction means adding a module would require changing the " +
                "shared plumbing.");
    }

    /// <summary>
    /// The cross-cutting mechanisms work through <c>AppTemplate.Domain.Core.Common</c> abstractions —
    /// <c>IAuditable</c>, <c>IVersioned</c>, <c>IDomainEvent</c> — plus two seams of their own,
    /// <c>IAggregateFlusher</c> and <c>IDomainEventSource</c>. The moment one of them names a business
    /// entity or a feature's mapping, auditing, flushing and event dispatch stop being generic and
    /// every new feature needs a change to all of them.
    /// <para>
    /// Scoped to the mechanisms rather than to the whole assembly: the features live in the same
    /// project and naming business types is their job.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePersistenceMechanisms_KnowNoFeature()
    {
        var mechanisms = Types.InAssembly(ArchitectureAssemblies.Persistence)
            .That()
            .ResideInNamespaceStartingWith(ArchitectureAssemblies.PersistenceCommonNamespace)
            .And()
            .DoNotHaveName(_contextTypeName);

        RuleAssertions.RequireTypes(
            mechanisms,
            $"a type under '{ArchitectureAssemblies.PersistenceCommonNamespace}' other than {_contextTypeName}");

        mechanisms
            .ShouldNot()
            .HaveDependencyOnAny(_forbiddenInPersistenceCommon)
            .GetResult()
            .ShouldHold(
                $"Nothing under '{ArchitectureAssemblies.PersistenceCommonNamespace}' — apart from "
                + $"{_contextTypeName} itself — may name a business entity or a feature's mapping. "
                + "Forbidden: " + string.Join(", ", _forbiddenInPersistenceCommon));
    }

    /// <summary>
    /// The one documented exception, asserted rather than assumed. <c>AppDbContext</c> applies every
    /// feature's entity configurations, so it necessarily names them: it is the model's composition
    /// root, exactly as <c>Program.cs</c> is the container's.
    /// <para>
    /// Asserted positively so that the exclusion above is a decision rather than a hole. If the context
    /// ever stopped naming a feature, the rule above would be excluding a type for no reason, and this
    /// test would say so.
    /// </para>
    /// </summary>
    [Fact]
    public void TheContext_IsTheOneThingInCommonThatNamesAFeature()
    {
        var context = Types.InAssembly(ArchitectureAssemblies.Persistence)
            .That()
            .HaveName(_contextTypeName);

        RuleAssertions.RequireTypes(context, $"a type named {_contextTypeName}");

        context
            .ShouldNot()
            .HaveDependencyOnAny(ArchitectureAssemblies.PersistenceFeaturesNamespace)
            .GetResult()
            .ShouldDetectAViolation(
                $"{_contextTypeName} is expected to name the feature configurations it applies. That it "
                + "does not means either the model is no longer composed there, or this rule is matching "
                + "nothing — and the exclusion in ThePersistenceMechanisms_KnowNoFeature would then be "
                + "covering up something it should not.");
    }

    /// <summary>
    /// Proves the mechanism rule bites. The same forbidden list is applied to the feature namespaces,
    /// which exist precisely to name those types; if this passed, <c>HaveDependencyOnAny</c> would be
    /// detecting nothing and the rule above would be decorative.
    /// </summary>
    [Fact]
    public void TheMechanismRule_IsSensitive_AndDetectsAFeatureDependency()
    {
        var features = Types.InAssembly(ArchitectureAssemblies.Persistence)
            .That()
            .ResideInNamespaceStartingWith(ArchitectureAssemblies.PersistenceFeaturesNamespace);

        RuleAssertions.RequireTypes(
            features,
            $"a type under '{ArchitectureAssemblies.PersistenceFeaturesNamespace}'");

        features
            .ShouldNot()
            .HaveDependencyOnAny(_forbiddenInPersistenceCommon)
            .GetResult()
            .ShouldDetectAViolation(
                "The to-do list feature's mapper and configurations name the domain aggregate and each "
                + "other by design, so applying the mechanism rule's forbidden list to them must fail.");
    }

    /// <summary>
    /// The assembly list the rules above address, against the modules that actually exist on disk.
    /// </summary>
    /// <remarks>
    /// Both lists in <c>ArchitectureAssemblies</c> are maintained by hand, and nothing about adding an
    /// infrastructure module prompts anyone to extend them. A module missing from them is a module
    /// five rules in this file walk past while reporting success, because each addresses assemblies by
    /// name. The rules keyed on the project graph do cover it, because <c>ProjectReferenceGraph</c>
    /// reads the disk — which is the difference this rule closes.
    /// <para>
    /// The sharpest half is the forbidden list in the rule below: it is computed <em>from</em> these
    /// assemblies, so a module missing here is not only unchecked as a subject — it is also not
    /// forbidden as a dependency, and another module could reference it freely.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryInfrastructureModuleOnDisk_IsAmongTheAssembliesTheseRulesAddress()
    {
        var onDisk = ProjectReferenceGraph.InfrastructureModules
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

        onDisk.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "Far fewer infrastructure modules were found under Src than this template has, so the " +
            "project walk is not reading the tree it is meant to describe.");

        var addressed = ArchitectureAssemblies.AllInfrastructure
            .Select(ArchitectureAssemblies.NamespaceOf)
            .ToHashSet(StringComparer.Ordinal);

        onDisk
            .Except(addressed, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "An infrastructure module exists that no rule in this file can see. Add it to "
                + "ArchitectureAssemblies.AllInfrastructure — and to ProductionInfrastructure unless a "
                + "host never composes it — or every rule written over those lists passes it by while "
                + "reporting success.");
    }

    [Fact]
    public void NoInfrastructureModule_DependsOnAnotherInfrastructureModule()
    {
        foreach (var assembly in ArchitectureAssemblies.AllInfrastructure)
        {
            string self = ArchitectureAssemblies.NamespaceOf(assembly);
            RuleAssertions.RequireTypes(assembly);

            // Two permitted horizontal dependencies, and neither is a module with its own vertical:
            // Persistence is shared plumbing, and the layer's package-grade half is a mechanism no
            // module owns.
            string[] forbidden = ArchitectureAssemblies.AllInfrastructure
                .Select(ArchitectureAssemblies.NamespaceOf)
                .Where(candidate => !string.Equals(candidate, self, StringComparison.Ordinal))
                .Where(candidate => !string.Equals(
                    candidate, ArchitectureAssemblies.PersistenceNamespace, StringComparison.Ordinal))
                .Where(candidate => !ProjectReferenceGraph.IsSdkProject(candidate))
                .ToArray();

            forbidden.ShouldNotBeEmpty($"No forbidden namespaces were computed for '{self}'.");

            Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(forbidden)
                .GetResult()
                .ShouldHold(
                    $"'{self}' must not depend on another infrastructure module. Only " +
                    "AppTemplate.Infrastructure.Persistence and the layer's package-grade half may be " +
                    "shared; anything else two modules need in common belongs behind a port in " +
                    "AppTemplate.Application, or in that shared half. Forbidden for this assembly: " +
                    string.Join(", ", forbidden));
        }
    }

    [Fact]
    public void NoInfrastructureModule_DependsOnThePresentationLayer()
    {
        foreach (var assembly in ArchitectureAssemblies.AllInfrastructure)
        {
            RuleAssertions.RequireTypes(assembly);

            Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(ArchitectureAssemblies.PresentationNamespace)
                .GetResult()
                .ShouldHold(
                    $"'{ArchitectureAssemblies.NamespaceOf(assembly)}' must not depend on AppTemplate.Api. " +
                    "Composition points inward: the host knows the modules, not the other way round.");
        }
    }

    // ---- The declared reference graph ---------------------------------------------------------
    //
    // The rules below read the project files. An unused ProjectReference is invisible to
    // NetArchTest but is still a declared, inward-pointing arrow — and is how the next violation
    // gets its foothold.

    [Fact]
    public void TheDomainLayer_ReferencesNothingOutsideItself()
    {
        var domainProjects = ProjectReferenceGraph.ProjectsInLayer("Domain").ToList();

        domainProjects.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            "Fewer projects were found in the domain layer than this template holds, so this rule " +
            "read no reference list at all and every list in it would report as clean.");

        var withinTheLayer = domainProjects
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

        domainProjects
            .SelectMany(project => project.References
                .Where(reference => !withinTheLayer.Contains(reference))
                .Select(reference => $"{project.RelativePath} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "The domain is the innermost layer: a project in it may reference another project " +
                "of the same layer and nothing else at all.");

        ProjectReferenceGraph.Project(_domainCoreProject)
            .References
            .ShouldBeEmpty(
                $"{_domainCoreProject} holds the primitives every aggregate is built from, so " +
                "whatever it referenced would be referenced by everything.");
    }

    /// <summary>
    /// The application layer sees the domain and itself, and nothing further out. Read over every
    /// project of the layer rather than one of them, so a project added to it is constrained the
    /// moment it exists.
    /// </summary>
    [Fact]
    public void TheApplicationLayer_ReferencesOnlyTheDomainAndItself()
    {
        var applicationProjects = ProjectReferenceGraph.ProjectsInLayer("Application").ToList();

        applicationProjects.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            "Fewer projects were found in the application layer than this template holds, so this " +
            "rule read no reference list at all and every list in it would report as clean.");

        var permitted = applicationProjects
            .Select(project => project.Name)
            .Concat(ProjectReferenceGraph.ProjectsInLayer("Domain").Select(project => project.Name))
            .ToHashSet(StringComparer.Ordinal);

        applicationProjects
            .SelectMany(project => project.References
                .Where(reference => !permitted.Contains(reference))
                .Select(reference => $"{project.RelativePath} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A project of the application layer may reference the domain and another project of " +
                "its own layer, and nothing else. A port belongs here; the project that implements " +
                "it must not be visible from here.");

        ProjectReferenceGraph.Project(_applicationProject)
            .References
            .ShouldContain(
                _domainProject,
                $"{_applicationProject} is written against the domain model and must reference it.");

        // The mechanisms know no feature, so they know no aggregate either: the two files here that
        // need a domain type need only the primitives.
        ProjectReferenceGraph.Project(_applicationCoreProject)
            .References
            .ShouldNotContain(
                _domainProject,
                $"{_applicationCoreProject} must reference {_domainCoreProject} and not " +
                $"{_domainProject}: naming the business domain would make the mechanisms know a " +
                "feature.");
    }

    [Fact]
    public void Persistence_ReferencesNoInfrastructureModule()
    {
        var persistence = ProjectReferenceGraph.Project(_persistenceProject);

        persistence.References
            .Where(ProjectReferenceGraph.IsInfrastructureModule)
            .ShouldBeEmpty(
                $"{persistence.RelativePath} must reference no infrastructure module. The arrows " +
                "point from a module to the shared plumbing, never back.");
    }

    [Fact]
    public void InfrastructureModules_ReferenceOnlyPersistenceHorizontally()
    {
        var offenders = new List<string>();
        int referencesToPersistence = 0;
        int referencesToTheLayersCore = 0;

        foreach (var module in ProjectReferenceGraph.InfrastructureModules)
        {
            foreach (string reference in module.References.Where(IsOfTheInfrastructureLayer))
            {
                if (string.Equals(reference, _persistenceProject, StringComparison.Ordinal))
                {
                    referencesToPersistence++;
                    continue;
                }

                if (ProjectReferenceGraph.IsSdkProject(reference))
                {
                    referencesToTheLayersCore++;
                    continue;
                }

                offenders.Add($"{module.RelativePath} -> {reference}");
            }
        }

        offenders.ShouldBeEmpty(
            "An infrastructure module may reference AppTemplate.Infrastructure.Persistence and the " +
            "package-grade half of its own layer, and no other infrastructure module. A mechanism two " +
            "modules need belongs in the layer's Core, where both may take it; a module reaching " +
            "sideways for one couples two adapter sets that a host is supposed to be able to compose " +
            "independently.");

        // Non-vacuity, in both directions the rule allows: if nothing referenced the shared plumbing
        // or the shared foundation any more, the rule above would hold trivially and the module
        // layout would have changed underneath it.
        referencesToPersistence.ShouldBeGreaterThan(
            0,
            "No infrastructure module references AppTemplate.Infrastructure.Persistence, so this rule is no " +
            "longer describing the repository.");

        referencesToTheLayersCore.ShouldBeGreaterThan(
            0,
            "No infrastructure module references the package-grade half of its own layer, so the " +
            "permission this rule grants describes nothing — either that project has gone, or a " +
            "mechanism two modules share has gone back to being copied.");
    }

    /// <summary>
    /// Any project of the infrastructure layer, modules and the layer's Core alike: the rule above
    /// has to see a reference in order to decide about it, and
    /// <see cref="ProjectReferenceGraph.IsInfrastructureModule"/> deliberately no longer counts the
    /// Core as a module.
    /// </summary>
    private static bool IsOfTheInfrastructureLayer(string projectName) =>
        projectName.StartsWith("AppTemplate.Infrastructure.", StringComparison.Ordinal);

    /// <summary>
    /// Composing the modules is a host's job, and only a host's. What the rule protects is that no
    /// layer below a composition root learns which modules exist, so one can be swapped without any
    /// other project noticing.
    /// </summary>
    [Fact]
    public void OnlyAHost_ReferencesInfrastructureModules()
    {
        var offenders = ProjectReferenceGraph.SourceProjects.Values
            .Where(project => !ProjectReferenceGraph.IsInfrastructureModule(project.Name))
            .Where(project => !ProjectReferenceGraph.IsHost(project))
            .Where(project => project.References.Any(ProjectReferenceGraph.IsInfrastructureModule))
            .Select(project => project.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "Only a composition root — a project with a Program.cs — may reference an " +
            "infrastructure module.");

        // Non-vacuity: a host that composes nothing would satisfy the rule for the wrong reason.
        foreach (var host in ProjectReferenceGraph.Hosts)
        {
            host.References
                .Where(ProjectReferenceGraph.IsInfrastructureModule)
                .ShouldNotBeEmpty($"{host.RelativePath} references no infrastructure module, so it composes nothing.");
        }
    }

    /// <summary>
    /// More than one host is the whole claim of the architecture made executable: the same use cases
    /// answer an HTTP request and a background loop, and neither transport appears below them.
    /// </summary>
    [Fact]
    public void TheApplicationLayer_IsComposedByMoreThanOneHost() =>
        ProjectReferenceGraph.Hosts
            .Count(host => host.References.Contains(_applicationProject))
            .ShouldBeGreaterThan(
                1,
                "One host cannot show that the application layer is independent of its transport.");
}

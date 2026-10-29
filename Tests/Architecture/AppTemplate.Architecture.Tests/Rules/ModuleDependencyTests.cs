using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ModuleDependencyTests
{
    private const string _apiProject = "AppTemplate.Api";
    private const string _domainProject = "AppTemplate.Domain";
    private const string _domainCoreProject = "AppTemplate.Domain.Core";
    private const string _applicationProject = "AppTemplate.Application";
    private const string _applicationCoreProject = "AppTemplate.Application.Core";
    private const string _persistenceProject = "AppTemplate.Infrastructure.Persistence";

    private const string _contextTypeName = "AppDbContext";

    private static readonly string[] _modulesThatMustNotReachIntoPersistence =
    [
        //#if (localAuth)
        ArchitectureAssemblies.IdentityInfrastructureNamespace,
        //#endif
        ArchitectureAssemblies.EmailInfrastructureNamespace,
        ArchitectureAssemblies.InMemoryInfrastructureNamespace,
    ];

    private static readonly string[] _forbiddenInPersistenceCommon =
    [
        ArchitectureAssemblies.DomainFeaturesNamespace,
        ArchitectureAssemblies.PersistenceFeaturesNamespace,
        ArchitectureAssemblies.ApplicationFeaturesNamespace,
        //#if (localAuth)
        ArchitectureAssemblies.ApplicationAuthNamespace,
        //#endif
    ];

    private static readonly (string Module, System.Reflection.Assembly Assembly)[] _moduleCommons =
    [
        //#if (localAuth)
        ("AppTemplate.Infrastructure.Auth", ArchitectureAssemblies.IdentityInfrastructure),
        //#endif
        //#if (files)
        ("AppTemplate.Infrastructure.Storage", ArchitectureAssemblies.StorageInfrastructure),
        //#endif
        ("AppTemplate.Infrastructure.Email", ArchitectureAssemblies.EmailInfrastructure),
        ("AppTemplate.Infrastructure.InMemory", ArchitectureAssemblies.InMemoryInfrastructure),
    ];

    private static readonly string[] _commonTypesAllowedToNameAFeature = [];

    [Fact]
    public void NoModuleCommon_CarriesAnException() =>
        _commonTypesAllowedToNameAFeature.ShouldBeEmpty(
            "Every type that implements a vertical's port or names a vertical's types files under "
            + "that vertical, not under the module's Common/.");

    [Fact]
    public void EveryModuleCommon_KnowsNoFeature()
    {
        int checkedModules = 0;

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

        checkedModules.ShouldBeGreaterThanOrEqualTo(
            2,
            $"Only {checkedModules} module Common namespaces yielded types. This rule is written "
            + "against every module that has one, so either a module lost its Common/ or the "
            + "namespace match is stale.");
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

    //#if (anyFeature)
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
                "A feature's persistence types name its application ports and each other by design, so "
                + "applying the mechanism rule's forbidden list to them must fail.");
    }
    //#endif

    [Fact]
    public void EveryInfrastructureModuleOnDisk_IsAmongTheAssembliesTheseRulesAddress()
    {
        var onDisk = ProjectReferenceGraph.InfrastructureModules
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

        onDisk.Count.ShouldBeGreaterThanOrEqualTo(
            3,
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
                    "AppTemplate.Infrastructure.Persistence and the layer's SDK half may be " +
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

    [Fact]
    public void TheApplicationLayer_ReferencesOnlyTheDomainAndItself()
    {
        var applicationProjects = ProjectReferenceGraph.ProjectsInLayer("Application").ToList();

        applicationProjects.Count.ShouldBeGreaterThanOrEqualTo(
            2,
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
    public void InfrastructureModules_ReferenceOnlyTheLayersCoreHorizontally()
    {
        var offenders = new List<string>();
        int referencesToTheLayersCore = 0;

        foreach (var module in ProjectReferenceGraph.InfrastructureModules)
        {
            foreach (string reference in module.References.Where(IsOfTheInfrastructureLayer))
            {
                if (ProjectReferenceGraph.IsSdkProject(reference))
                {
                    referencesToTheLayersCore++;
                    continue;
                }

                offenders.Add($"{module.RelativePath} -> {reference}");
            }
        }

        offenders.ShouldBeEmpty(
            "An infrastructure module may reference the SDK half of its own layer, and no " +
            "other project of this layer. A mechanism two modules need belongs in that Core, where " +
            "both may take it; a module reaching sideways for one couples two adapter sets that a " +
            "host is supposed to be able to compose independently.");

        referencesToTheLayersCore.ShouldBeGreaterThan(
            0,
            "No infrastructure module references the SDK half of its own layer, so the " +
            "permission this rule grants describes nothing.");
    }

    private static bool IsOfTheInfrastructureLayer(string projectName) =>
        projectName.StartsWith("AppTemplate.Infrastructure.", StringComparison.Ordinal);

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

        foreach (var host in ProjectReferenceGraph.Hosts)
        {
            host.References
                .Where(ProjectReferenceGraph.IsInfrastructureModule)
                .ShouldNotBeEmpty($"{host.RelativePath} references no infrastructure module, so it composes nothing.");
        }
    }

    [Fact]
    public void NoHost_ReferencesAnotherHost()
    {
        var hosts = ProjectReferenceGraph.Hosts.ToList();
        var names = hosts.Select(host => host.Name).ToHashSet(StringComparer.Ordinal);

        hosts.ShouldNotBeEmpty("No host was found, so this rule would pass having checked nothing.");

        hosts
            .SelectMany(host => host.References
                .Where(names.Contains)
                .Select(reference => $"{host.RelativePath} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "Each host composes itself. One that referenced another would reach its jobs and its development "
                + "migrations, which run in the API alone, and every registration of its own.");
    }

    [Fact]
    public void EveryHost_ComposesTheApplicationLayer()
    {
        var hosts = ProjectReferenceGraph.Hosts.ToList();

        hosts.ShouldNotBeEmpty("No host was found, so this rule would pass having checked nothing.");

        hosts
            .Where(host => !host.References.Contains(_applicationProject))
            .Select(host => host.RelativePath)
            .ShouldBeEmpty("A host that does not compose the application layer runs none of its use cases.");
    }

    [Fact]
    public void EveryClient_ReachesTheCoreThroughTheApiAlone()
    {
        var withinTheClients = ProjectReferenceGraph.ProjectsInLayer("Clients")
            .Select(project => project.Name)
            .ToHashSet(StringComparer.Ordinal);

        ProjectReferenceGraph.ProjectsInLayer("Clients")
            .Concat(ProjectReferenceGraph.Clients.Where(client => !withinTheClients.Contains(client.Name)))
            .SelectMany(client => client.References
                .Where(reference => !withinTheClients.Contains(reference) && reference != $"{client.Name}.Core")
                .Select(reference => $"{client.RelativePath} -> {reference}"))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A client runs on another machine than the API and speaks to it over HTTP alone, and so does a presentation "
                + "that calls the API through it, with the <Name>.Core that holds its view models: a reference to a domain, "
                + "application, infrastructure or other presentation project would ship the server's code to it (ADR 0022, ADR 0023).");
    }
}

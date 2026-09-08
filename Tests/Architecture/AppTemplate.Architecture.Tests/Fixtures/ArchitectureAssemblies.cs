using System.Reflection;
using AppTemplate.Application;
using AppTemplate.Application.Auth;
using AppTemplate.Application.Core;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Presentation.Core;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// The assemblies every fitness rule is written against, each anchored on a real type so the
/// assembly is genuinely loaded before a rule runs.
/// <para>
/// This indirection is not decoration. NetArchTest evaluates a rule over the type set it is
/// given, and an empty type set satisfies every <c>ShouldNot</c> condition — so a rule aimed at
/// an assembly that was never loaded, or at a namespace that has since been renamed, passes and
/// buys nothing. <see cref="RuleAssertions.RequireTypes(Assembly)"/> is called before each
/// assertion so that a rule which has stopped matching anything fails loudly instead of turning
/// green.
/// </para>
/// <para>
/// Each assembly resolves on first use rather than in this type's initializer, and that is
/// load-bearing. An anchor asserts the assembly a type compiles into, so a type that changes
/// assembly makes its anchor throw — and a throw in a type initializer is a
/// <see cref="TypeInitializationException"/> on every member of this class, which fails every rule
/// file that reads any assembly at all. Resolved lazily, a stale anchor fails the rules that
/// address that one assembly and names it.
/// </para>
/// </summary>
internal static class ArchitectureAssemblies
{
    internal const string DomainNamespace = "AppTemplate.Domain";
    internal const string DomainCoreNamespace = "AppTemplate.Domain.Core";
    internal const string ApplicationNamespace = "AppTemplate.Application";
    internal const string ApplicationCoreNamespace = "AppTemplate.Application.Core";
    internal const string ApplicationAuthNamespace = "AppTemplate.Application.Auth";
    internal const string InfrastructureNamespace = "AppTemplate.Infrastructure";
    internal const string PresentationNamespace = "AppTemplate.Api";
    internal const string PresentationCoreNamespace = "AppTemplate.Presentation.Core";

    internal const string PersistenceNamespace = "AppTemplate.Infrastructure.Persistence";
    internal const string IdentityInfrastructureNamespace = "AppTemplate.Infrastructure.Identity";
    internal const string EmailInfrastructureNamespace = "AppTemplate.Infrastructure.Email";
    internal const string InMemoryInfrastructureNamespace = "AppTemplate.Infrastructure.InMemory";
    internal const string StorageInfrastructureNamespace = "AppTemplate.Infrastructure.Storage";

    /// <summary>
    /// The cross-cutting mechanisms inside the persistence project: the interceptor pipeline, the unit
    /// of work, the clock, the event dispatcher, the mapping seam. Everything here must work
    /// through <c>AppTemplate.Domain.Core.Common</c> abstractions, so that adding a feature cannot
    /// require a change to any of it.
    /// </summary>
    internal const string PersistenceCommonNamespace = "AppTemplate.Infrastructure.Persistence.Common";

    /// <summary>
    /// The per-feature half of the persistence project: models, configurations, mappers, repositories,
    /// queries and stores. These <em>do</em> name business types — that is their job.
    /// </summary>
    internal const string PersistenceFeaturesNamespace = "AppTemplate.Infrastructure.Persistence.Features";

    /// <summary>
    /// Every feature's domain surface: its entities, its value objects, its events. Named at the
    /// level of <c>Features</c> rather than of one feature, because a mechanism that must not know
    /// about to-do lists must not know about reminders or files either, and a list of one feature
    /// only guards the feature somebody happened to write it for.
    /// </summary>
    internal const string DomainFeaturesNamespace = "AppTemplate.Domain.Features";

    /// <summary>
    /// Every feature's application-layer surface: its ports, its DTOs, its errors. A cross-cutting
    /// persistence mechanism that names one of these is a feature adapter wearing the shared
    /// mechanisms' clothes — <c>ReminderDiagnostics</c> is that shape: an OpenTelemetry counter with
    /// no connection to the database at all, which would sit unnoticed under
    /// <c>Common/Observability/</c>, since it depends on neither of the two namespaces above.
    /// </summary>
    internal const string ApplicationFeaturesNamespace = "AppTemplate.Application.Features";

    /// <summary>
    /// Anchored on a feature entity, because the business half of the domain declares nothing
    /// cross-cutting: the primitives are one project inwards. A derived project that removes every
    /// example feature removes this anchor's type with them, and then
    /// <see cref="RuleAssertions.RequireTypes(Assembly)"/> is the thing that says the assembly holds
    /// nothing left to check.
    /// </summary>
    private static readonly Lazy<Assembly> _domain = new(() => Anchor(typeof(StoredFile), DomainNamespace));

    private static readonly Lazy<Assembly> _domainCore =
        new(() => Anchor(typeof(IAggregateRoot), DomainCoreNamespace));

    private static readonly Lazy<Assembly> _application =
        new(() => Anchor(typeof(ApplicationModule), ApplicationNamespace));

    private static readonly Lazy<Assembly> _applicationCore =
        new(() => Anchor(typeof(ApplicationCoreModule), ApplicationCoreNamespace));

    private static readonly Lazy<Assembly> _applicationAuth =
        new(() => Anchor(typeof(ApplicationAuthModule), ApplicationAuthNamespace));

    private static readonly Lazy<Assembly> _presentationCore =
        new(() => Anchor(typeof(PresentationCoreModule), PresentationCoreNamespace));

    private static readonly Lazy<Assembly> _persistence =
        new(() => Anchor(typeof(PersistenceModule), PersistenceNamespace));

    private static readonly Lazy<Assembly> _identityInfrastructure =
        new(() => Anchor(typeof(IdentityModule), IdentityInfrastructureNamespace));

    private static readonly Lazy<Assembly> _emailInfrastructure =
        new(() => Anchor(typeof(EmailModule), EmailInfrastructureNamespace));

    private static readonly Lazy<Assembly> _inMemoryInfrastructure =
        new(() => Anchor(typeof(InMemoryModule), InMemoryInfrastructureNamespace));

    private static readonly Lazy<Assembly> _storageInfrastructure =
        new(() => Anchor(typeof(StorageModule), StorageInfrastructureNamespace));

    private static readonly Lazy<IReadOnlyList<Assembly>> _allInfrastructure =
        new(() => LayerAssemblies("Infrastructure"));

    private static readonly Lazy<IReadOnlyList<Assembly>> _productionInfrastructure =
        new(() => LayerAssemblies("Infrastructure", module => ProjectReferenceGraph.Hosts
            .Any(host => host.References.Contains(module.Name))));

    private static readonly Lazy<IReadOnlyList<Assembly>> _applicationLayer =
        new(() => LayerAssemblies("Application"));

    private static readonly Lazy<IReadOnlyList<Assembly>> _domainLayer =
        new(() => LayerAssemblies("Domain"));

    internal static Assembly Domain => _domain.Value;

    internal static Assembly DomainCore => _domainCore.Value;

    internal static Assembly Application => _application.Value;

    internal static Assembly ApplicationCore => _applicationCore.Value;

    internal static Assembly ApplicationAuth => _applicationAuth.Value;

    internal static Assembly PresentationCore => _presentationCore.Value;

    internal static Assembly Persistence => _persistence.Value;

    internal static Assembly IdentityInfrastructure => _identityInfrastructure.Value;

    internal static Assembly EmailInfrastructure => _emailInfrastructure.Value;

    internal static Assembly InMemoryInfrastructure => _inMemoryInfrastructure.Value;

    internal static Assembly StorageInfrastructure => _storageInfrastructure.Value;

    /// <summary>
    /// The infrastructure modules a host composes, which is what makes one a production module: the
    /// criterion is a host's own <c>ProjectReference</c>, so the set follows the tree.
    /// <c>AppTemplate.Infrastructure.InMemory</c> falls outside it because no host names it — it
    /// exists to replace their adapters in a test host, and its doubles are public by design because
    /// a test has to reach them.
    /// </summary>
    internal static IReadOnlyList<Assembly> ProductionInfrastructure => _productionInfrastructure.Value;

    /// <summary>Every infrastructure module on disk, read from the project graph rather than listed.</summary>
    internal static IReadOnlyList<Assembly> AllInfrastructure => _allInfrastructure.Value;

    /// <summary>
    /// Every project of the application layer, and every project of the domain. A layer split across
    /// projects declares its ports across all of them, so a rule addressing "the application layer"
    /// has to read the whole of it or it checks the half it was written for.
    /// </summary>
    internal static IReadOnlyList<Assembly> ApplicationLayer => _applicationLayer.Value;

    internal static IReadOnlyList<Assembly> DomainLayer => _domainLayer.Value;

    /// <summary>
    /// The root namespace an assembly owns, which is its simple name: the convention this
    /// repository checks elsewhere is that a project's name and its root namespace are one string.
    /// </summary>
    internal static string NamespaceOf(Assembly assembly) =>
        assembly.GetName().Name
        ?? throw new InvalidOperationException("A product assembly has no simple name.");

    /// <summary>
    /// The projects one layer holds, optionally narrowed by <paramref name="keep"/>, resolved by
    /// name. Empty is refused: a set read off the tree that came back with nothing would hand every
    /// rule over it a population of none, and every <c>ShouldNot</c> in them would hold.
    /// </summary>
    private static IReadOnlyList<Assembly> LayerAssemblies(
        string layerFolder,
        Func<ProjectNode, bool>? keep = null)
    {
        var projects = ProjectReferenceGraph
            .ProjectsInLayer(layerFolder)
            .Where(keep ?? (_ => true))
            .ToList();

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"The project graph reports no project under 'Src\\{layerFolder}' for this set. " +
                "Every rule written over it would pass by reading nothing.");
        }

        return [.. projects.Select(project => Load(project.Name))];
    }

    /// <summary>
    /// Resolves an assembly by the name its project carries, which is the name the project graph
    /// knows it by. Throws rather than skipping: an assembly the test's load context cannot reach is
    /// a missing <c>ProjectReference</c>, and a rule aimed at it would report success.
    /// </summary>
    private static Assembly Load(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"'{assemblyName}' exists under Src but could not be loaded by name. " +
                "AppTemplate.Architecture.Tests has to reference every project it addresses, or the " +
                "rules aimed at this one check nothing.",
                exception);
        }
    }

    /// <summary>
    /// Loads an assembly through a type that has to exist for this project to compile, then
    /// cross-checks it against <see cref="Assembly.Load(AssemblyName)"/> under the expected simple
    /// name — so a renamed assembly fails here rather than silently disabling a rule.
    /// </summary>
    private static Assembly Anchor(Type anchor, string expectedAssemblyName)
    {
        var assembly = anchor.Assembly;
        string? actualName = assembly.GetName().Name;

        if (!string.Equals(actualName, expectedAssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected '{anchor.FullName}' to live in assembly '{expectedAssemblyName}' but it " +
                "lives in '" + actualName + "'. The architecture rules address assemblies and " +
                "namespaces by name; fix the anchor before trusting them.");
        }

        // Redundant on purpose: proves the assembly is resolvable by name from the test's load
        // context, which is what every rule below relies on.
        var loaded = Assembly.Load(new AssemblyName(expectedAssemblyName));

        if (!ReferenceEquals(loaded, assembly))
        {
            throw new InvalidOperationException(
                $"'{expectedAssemblyName}' resolved to two different assembly instances.");
        }

        return assembly;
    }
}

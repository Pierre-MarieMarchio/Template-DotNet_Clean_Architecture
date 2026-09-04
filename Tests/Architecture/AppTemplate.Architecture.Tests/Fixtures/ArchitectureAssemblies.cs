using System.Reflection;
using AppTemplate.Application;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence;

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
/// </summary>
internal static class ArchitectureAssemblies
{
    internal const string DomainNamespace = "AppTemplate.Domain";
    internal const string ApplicationNamespace = "AppTemplate.Application";
    internal const string InfrastructureNamespace = "AppTemplate.Infrastructure";
    internal const string PresentationNamespace = "AppTemplate.Api";

    internal const string PersistenceNamespace = "AppTemplate.Infrastructure.Persistence";
    internal const string IdentityInfrastructureNamespace = "AppTemplate.Infrastructure.Identity";
    internal const string EmailInfrastructureNamespace = "AppTemplate.Infrastructure.Email";
    internal const string InMemoryInfrastructureNamespace = "AppTemplate.Infrastructure.InMemory";

    /// <summary>
    /// The cross-cutting mechanisms inside the persistence project: the interceptor pipeline, the unit
    /// of work, the clock, the event dispatcher, the mapping seam. Everything here must work through
    /// <c>AppTemplate.Domain.Common</c> abstractions, so that adding a feature cannot require a change to any
    /// of it.
    /// </summary>
    internal const string PersistenceCommonNamespace = "AppTemplate.Infrastructure.Persistence.Common";

    /// <summary>
    /// The per-feature half of the persistence project: models, configurations, mappers, repositories,
    /// queries and stores. These <em>do</em> name business types — that is their job.
    /// </summary>
    internal const string PersistenceFeaturesNamespace = "AppTemplate.Infrastructure.Persistence.Features";

    /// <summary>The business entities the cross-cutting mechanisms must stay free of.</summary>
    internal const string TodoListsDomainNamespace = "AppTemplate.Domain.Features.TodoLists";

    internal static Assembly Domain { get; } = Anchor(typeof(IAggregateRoot), DomainNamespace);

    internal static Assembly Application { get; } = Anchor(typeof(ServiceRegistration), ApplicationNamespace);

    internal static Assembly Persistence { get; } = Anchor(typeof(PersistenceModule), PersistenceNamespace);

    internal static Assembly IdentityInfrastructure { get; } =
        Anchor(typeof(IdentityModule), IdentityInfrastructureNamespace);

    internal static Assembly EmailInfrastructure { get; } =
        Anchor(typeof(EmailModule), EmailInfrastructureNamespace);

    internal static Assembly InMemoryInfrastructure { get; } =
        Anchor(typeof(InMemoryModule), InMemoryInfrastructureNamespace);

    /// <summary>
    /// The infrastructure modules the API composes. <c>AppTemplate.Infrastructure.InMemory</c> is not one
    /// of them: it exists to replace their adapters in a test host, and its doubles are public by
    /// design because a test has to reach them.
    /// </summary>
    internal static IReadOnlyList<Assembly> ProductionInfrastructure { get; } =
    [
        Persistence,
        IdentityInfrastructure,
        EmailInfrastructure,
    ];

    internal static IReadOnlyList<Assembly> AllInfrastructure { get; } =
    [
        Persistence,
        IdentityInfrastructure,
        EmailInfrastructure,
        InMemoryInfrastructure,
    ];

    /// <summary>The namespace each infrastructure assembly owns, keyed by the assembly itself.</summary>
    internal static string NamespaceOf(Assembly infrastructureAssembly) =>
        infrastructureAssembly.GetName().Name
        ?? throw new InvalidOperationException("An infrastructure assembly has no simple name.");

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

using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Collections;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// Every contract the application layer declares for something else to satisfy, discovered from the
/// namespaces the convention puts them in.
/// <para>
/// This exists because the same list used to be written out by hand in two places, eleven entries
/// long, while the repository had twenty-nine contracts. Eighteen of them — every Reminders port,
/// most of Auth, and <c>IIdempotencyStore</c> — were covered by no rule at all, not because anyone
/// decided they should not be, but because adding a port and remembering to extend two arrays are
/// different acts. <c>PortConventionTests</c> had already written the discovery and said why:
/// "from the namespaces the convention puts them in rather than from a list a change could forget."
/// It is now the only copy, and the rules that used the arrays read it instead.
/// </para>
/// </summary>
internal static class ApplicationPorts
{
    /// <summary>
    /// <c>AppTemplate.Application.Features.&lt;Vertical&gt;.Ports.&lt;Port&gt;</c>. A port owns a
    /// folder holding its interface and the messages that cross it, so the match has to reach past
    /// <c>Ports</c> rather than stop at it.
    /// </summary>
    private const string _featurePortNamespacePrefix = "AppTemplate.Application.Features.";

    private const string _featurePortNamespaceSegment = ".Ports";

    /// <summary>
    /// The cross-cutting half: <c>Common.Abstractions</c> for the clock, the mail relay, the unit of
    /// work and the caller, plus the rest of <c>Common</c> — which is where <c>IIdempotencyStore</c>
    /// lives, and where a narrower match on <c>Common.Abstractions</c> alone stopped seeing it.
    /// </summary>
    private const string _crossCuttingPortNamespacePrefix = "AppTemplate.Application.Common";

    /// <summary>
    /// Public interfaces in the application layer that are not ports for a module to satisfy.
    /// <para>
    /// <see cref="IUseCase"/> and its arities are the marker registration discovers use cases
    /// through; <see cref="IDomainEventConsumer"/> is implemented by the application layer rather
    /// than consumed from it; <see cref="ICollectionPolicy"/> is a strategy whose implementations
    /// are application types reached through a static instance, never resolved from the container.
    /// </para>
    /// </summary>
    private static readonly Type[] _notPorts =
    [
        typeof(IUseCase),
        typeof(IUseCase<>),
        typeof(IUseCase<,>),
        typeof(IDomainEventConsumer),
        typeof(IDomainEventConsumer<>),
        typeof(ICollectionPolicy),
    ];

    /// <summary>
    /// The ports declared in <c>AppTemplate.Application</c>, ordered so a failure message reads the
    /// same way twice.
    /// </summary>
    internal static IReadOnlyList<Type> Declared { get; } =
        [.. ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => !_notPorts.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type))
            .Where(type => type.Namespace is not null && IsPortNamespace(type.Namespace))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    /// <summary>
    /// The repository contracts, which live in the Domain because their signatures name nothing but
    /// aggregates. They are ports in every sense that matters to a composed host — a
    /// module satisfies them and the container has to resolve them — so a rule about "every port"
    /// that skipped them would be checking the easier half.
    /// </summary>
    internal static IReadOnlyList<Type> DomainRepositories { get; } =
        [.. ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => type.Namespace?.EndsWith(".Repositories", StringComparison.Ordinal) == true)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    /// <summary>Both halves: what a host must be able to resolve for the application layer to run.</summary>
    internal static IReadOnlyList<Type> All { get; } =
        [.. Declared.Concat(DomainRepositories).OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static bool IsPortNamespace(string @namespace) =>
        (@namespace.StartsWith(_featurePortNamespacePrefix, StringComparison.Ordinal)
            && @namespace.Contains(_featurePortNamespaceSegment, StringComparison.Ordinal))
        || @namespace.StartsWith(_crossCuttingPortNamespacePrefix, StringComparison.Ordinal);
}

using AppTemplate.Application.Common.Events;
using AppTemplate.Application.Common.Policies;
using AppTemplate.Application.Common.UseCases;

namespace AppTemplate.Architecture.Tests.Fixtures;

/// <summary>
/// Every contract the application layer declares for something else to satisfy, discovered from the
/// namespaces the convention puts them in.
/// <para>
/// Discovered rather than listed. A port is recognised by the namespace the convention puts it in,
/// so adding one under <c>Features/&lt;F&gt;/Ports/</c> or under <c>Common/</c> brings it into every
/// rule that reads this — no array to remember. The match on <c>Common</c> is deliberately the whole
/// of it rather than <c>Common.Abstractions</c> alone, because <c>IIdempotencyStore</c> lives in
/// <c>Common.Idempotency</c>.
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
    /// lives, and which a narrower match on <c>Common.Abstractions</c> alone would not reach.
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
    /// What this discovery deliberately drops. Exposed because the difference between "every public
    /// interface in a port namespace" and "every port" is exactly this list: a rule comparing the
    /// discovery against the source tree has to add it back, and one that could not name it would
    /// re-derive the exclusion and drift from it.
    /// </summary>
    internal static IReadOnlyList<Type> NotPorts => _notPorts;

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

using System.Reflection;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.UseCases;

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
    private const string _featurePortNamespaceSegment = ".Ports";

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

    private static readonly Lazy<IReadOnlyList<Type>> _declared = new(() =>
        [.. ArchitectureAssemblies.ApplicationLayer
            .SelectMany(PortsIn)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)]);

    private static readonly Lazy<IReadOnlyList<Type>> _domainRepositories = new(() =>
        [.. ArchitectureAssemblies.DomainLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => type.Namespace?.EndsWith(".Repositories", StringComparison.Ordinal) == true)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)]);

    private static readonly Lazy<IReadOnlyList<Type>> _all = new(() =>
        [.. Declared.Concat(DomainRepositories).OrderBy(type => type.FullName, StringComparer.Ordinal)]);

    /// <summary>
    /// The ports the application layer declares, across every project of it, ordered so a failure
    /// message reads the same way twice.
    /// </summary>
    internal static IReadOnlyList<Type> Declared => _declared.Value;

    /// <summary>
    /// The repository contracts, which live in the Domain because their signatures name nothing but
    /// aggregates. They are ports in every sense that matters to a composed host — a
    /// module satisfies them and the container has to resolve them — so a rule about "every port"
    /// that skipped them would be checking the easier half.
    /// </summary>
    internal static IReadOnlyList<Type> DomainRepositories => _domainRepositories.Value;

    /// <summary>Both halves: what a host must be able to resolve for the application layer to run.</summary>
    internal static IReadOnlyList<Type> All => _all.Value;

    private static IEnumerable<Type> PortsIn(Assembly assembly)
    {
        string root = assembly.GetName().Name
            ?? throw new InvalidOperationException("An application assembly has no simple name.");

        return assembly
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => !_notPorts.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type))
            .Where(type => type.Namespace is not null && IsPortNamespace(root, type.Namespace));
    }

    /// <summary>
    /// Whether a namespace is one the port convention puts a port in, judged against the root
    /// namespace of the assembly that declares it — <c>&lt;Root&gt;.Features.&lt;Vertical&gt;.Ports</c>
    /// or <c>&lt;Root&gt;.Common</c>.
    /// <para>
    /// Anchored on the declaring assembly's full name rather than on a shared prefix. A prefix short
    /// enough to cover several projects of one layer also covers the projects of another layer whose
    /// names begin the same way, and the resulting population is wrong in a direction no rule
    /// reports: it grows.
    /// </para>
    /// </summary>
    private static bool IsPortNamespace(string root, string @namespace) =>
        (@namespace.StartsWith($"{root}.Features.", StringComparison.Ordinal)
            && @namespace.Contains(_featurePortNamespaceSegment, StringComparison.Ordinal))
        || @namespace.StartsWith($"{root}.Common", StringComparison.Ordinal);
}

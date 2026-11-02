using System.Reflection;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class ApplicationPorts
{
    private const string _featurePortNamespaceSegment = ".Ports";

    private static readonly Type[] _notPorts =
    [
        typeof(IUseCase),
        typeof(IUseCase<>),
        typeof(IUseCase<,>),
        typeof(IDomainEventConsumer),
        typeof(IDomainEventConsumer<>),
        typeof(ICollectionPolicy),
        typeof(ICollectionQuery),
    ];

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

    internal static IReadOnlyList<Type> Declared => _declared.Value;

    internal static IReadOnlyList<Type> DomainRepositories => _domainRepositories.Value;

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

    private static bool IsPortNamespace(string root, string @namespace) =>
        (@namespace.StartsWith($"{root}.Features.", StringComparison.Ordinal)
            && @namespace.Contains(_featurePortNamespaceSegment, StringComparison.Ordinal))
        || @namespace.StartsWith($"{root}.Common", StringComparison.Ordinal);
}

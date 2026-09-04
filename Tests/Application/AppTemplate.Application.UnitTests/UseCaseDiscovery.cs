using AppTemplate.Application.Common.Abstractions;
using Shouldly;

namespace AppTemplate.Application.UnitTests;

/// <summary>
/// Finds the layer's use cases the same way registration does, derived here independently so that a
/// test asserts what the container should hold rather than echoing what it does hold.
/// </summary>
internal static class UseCaseDiscovery
{
    internal static IReadOnlyList<Type> Implementations { get; } =
        [.. typeof(ApplicationModule).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IUseCase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    internal static Type ContractOf(Type implementation)
    {
        ArgumentNullException.ThrowIfNull(implementation);

        var contracts = implementation.GetInterfaces()
            .Where(candidate => candidate != typeof(IUseCase)
                && !candidate.IsGenericType
                && typeof(IUseCase).IsAssignableFrom(candidate))
            .ToArray();

        contracts.Length.ShouldBe(
            1,
            $"'{implementation.FullName}' must declare exactly one named use-case interface, " +
            $"but declares {contracts.Length}.");

        return contracts[0];
    }
}

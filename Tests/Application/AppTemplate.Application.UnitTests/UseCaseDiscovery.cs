using AppTemplate.Application.Core.Common.UseCases;
using Shouldly;

namespace AppTemplate.Application.UnitTests;

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

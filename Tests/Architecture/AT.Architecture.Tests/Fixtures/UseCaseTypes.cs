using AppTemplate.Application.Core.Common.UseCases;
using Mediator;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class UseCaseTypes
{
    private static readonly Type[] _handlerContracts = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    internal static IReadOnlyList<Type> InTheApplicationLayer { get; } =
        [.. ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(IsUseCase)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    internal static bool IsUseCase(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && (HandlerContractsOf(type).Length > 0 || typeof(IUseCase).IsAssignableFrom(type));

    internal static Type ContractOf(Type implementation)
    {
        if (HandlerContractsOf(implementation) is [var handled])
        {
            return handled;
        }

        return implementation.GetInterfaces()
            .Where(candidate => candidate != typeof(IUseCase)
                && !candidate.IsGenericType
                && typeof(IUseCase).IsAssignableFrom(candidate))
            .ToArray() is [var contract]
            ? contract
            : throw new InvalidOperationException(
                $"'{implementation.FullName}' must handle exactly one message.");
    }

    internal static Type[] HandlerContractsOf(Type type) =>
        [.. type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType
                && _handlerContracts.Contains(candidate.GetGenericTypeDefinition()))];
}

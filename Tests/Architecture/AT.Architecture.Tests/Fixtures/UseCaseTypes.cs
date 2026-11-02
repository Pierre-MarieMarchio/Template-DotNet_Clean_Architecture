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

    internal static IReadOnlyList<Type> MessagesInTheApplicationLayer { get; } =
        [.. ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IMessage).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    internal static bool IsUseCase(Type type) =>
        type is { IsClass: true, IsAbstract: false } && HandlerContractsOf(type).Length > 0;

    internal static Type ContractOf(Type implementation) =>
        HandlerContractsOf(implementation) is [var contract]
            ? contract
            : throw new InvalidOperationException(
                $"'{implementation.FullName}' must handle exactly one message.");

    internal static Type[] HandlerContractsOf(Type type) =>
        [.. type.GetInterfaces()
            .Where(candidate => candidate.IsGenericType
                && _handlerContracts.Contains(candidate.GetGenericTypeDefinition()))];
}

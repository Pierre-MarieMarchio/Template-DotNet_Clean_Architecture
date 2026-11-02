using System.Reflection;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.UseCases;
//#if (anyFeature)
using AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;
//#endif
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Application.Core;

public static class ApplicationCoreModule
{
    public static IServiceCollection AddPurgeExpiredIdempotencyKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddUseCases([typeof(PurgeExpiredIdempotencyKeysUseCase)]);
    }
    //#if (anyFeature)

    public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddUseCases([typeof(SweepDeletedAccountDataUseCase)]);
    }
    //#endif

    public static IServiceCollection AddDomainEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where TConsumer : class, IDomainEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDomainEventConsumer<TEvent>, TConsumer>();

        return services;
    }

    public static IServiceCollection AddUseCasesFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return services.AddUseCases(assembly.GetTypes());
    }

    public static IServiceCollection AddUseCases(this IServiceCollection services, IEnumerable<Type> candidates)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidates);

        var implementations = candidates
            .Where(candidate => candidate is { IsClass: true, IsAbstract: false }
                && typeof(IUseCase).IsAssignableFrom(candidate))
            .ToArray();

        foreach (var implementation in implementations)
        {
            services.AddScoped(ContractOf(implementation), implementation);
        }

        return services;
    }

    private static Type ContractOf(Type implementation)
    {
        var contracts = Array.FindAll(
            implementation.GetInterfaces(),
            candidate => candidate != typeof(IUseCase)
                && !candidate.IsGenericType
                && typeof(IUseCase).IsAssignableFrom(candidate));

        if (contracts.Length != 1)
        {
            throw new InvalidOperationException(
                $"'{implementation.FullName}' declares {contracts.Length} named use-case interfaces " +
                "but must declare exactly one: " +
                (contracts.Length == 0
                    ? "give it an interface of its own deriving from IUseCase<,>."
                    : $"found {string.Join(", ", contracts.Select(contract => contract.Name))}, and " +
                      "picking one of them would be a guess."));
        }

        return contracts[0];
    }
}

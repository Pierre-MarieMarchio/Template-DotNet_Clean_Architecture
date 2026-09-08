using System.Reflection;
using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Application.Core;

/// <summary>
/// What a host composes to use this project: the registration helpers every application project
/// binds its own types through, and the one use case this project declares.
/// <para>
/// There is deliberately no single <c>AddApplicationCore()</c> covering all of it. This project
/// declares ports, results and markers — things a host implements or names, not things a container
/// resolves — so an umbrella call would register nothing, and a call that does nothing is worse
/// than its absence: it reads like the seam that makes the project work.
/// </para>
/// </summary>
public static class ApplicationCoreModule
{
    /// <summary>
    /// Registers the expired-idempotency-key purge.
    /// <para>
    /// Separate from anything else on purpose. It resolves <c>IIdempotencyStore</c> and
    /// <c>IDateTimeProvider</c>, so a host with neither a maintenance endpoint nor a maintenance
    /// loop would be made to supply two adapters it has no use for. Both hosts in this template do
    /// schedule it; a derived project that drops idempotency drops this line with it.
    /// </para>
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPurgeExpiredIdempotencyKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddUseCases([typeof(PurgeExpiredIdempotencyKeysUseCase)]);
    }

    /// <summary>
    /// Binds a consumer to one event type. Registration is explicit rather than scanned so that a
    /// consumer which is never reached is a compile-time absence rather than a silent one.
    /// </summary>
    /// <typeparam name="TEvent">The event the consumer answers.</typeparam>
    /// <typeparam name="TConsumer">The consumer to bind.</typeparam>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDomainEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where TConsumer : class, IDomainEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDomainEventConsumer<TEvent>, TConsumer>();

        return services;
    }

    /// <summary>Registers every use case one assembly declares.</summary>
    /// <param name="services">The container being built.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddUseCasesFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        return services.AddUseCases(assembly.GetTypes());
    }

    /// <summary>
    /// Registers each <see cref="IUseCase"/> implementation under the single named interface it
    /// declares.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="candidates">The types to consider. Anything that is not a concrete
    /// <see cref="IUseCase"/> is skipped.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// A use case declares no named interface, or several: there is no one service type to bind.
    /// </exception>
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

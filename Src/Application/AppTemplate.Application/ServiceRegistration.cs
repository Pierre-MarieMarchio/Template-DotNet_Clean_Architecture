using System.Reflection;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Access;
using AppTemplate.Application.Features.TodoLists.Consumers;
using AppTemplate.Application.Features.TodoLists.Validators;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Events;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Application;

public static class ServiceRegistration
{
    /// <summary>
    /// Takes no <c>IConfiguration</c> deliberately: nothing in this layer reads settings, and
    /// accepting configuration invites the infrastructure knowledge the layer exists to avoid.
    /// </summary>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssemblyContaining<CreateTodoListCommandValidator>(
            lifetime: ServiceLifetime.Scoped,
            includeInternalTypes: true);

        services.AddUseCasesFrom(typeof(ServiceRegistration).Assembly);

        // Not a use case: it has no request/response shape of its own, so the marker-based
        // discovery above never sees it. Bound explicitly, like the domain-event consumer below.
        services.AddScoped<ITodoListAccess, TodoListAccess>();

        services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, LogTodoItemCompletedConsumer>();

        return services;
    }

    /// <summary>
    /// Binds a consumer to one event type. Registration is explicit rather than scanned so that a
    /// consumer which is never reached is a compile-time absence rather than a silent one.
    /// </summary>
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

    /// <summary>
    /// Registers each <see cref="IUseCase"/> implementation under the single named interface it
    /// declares.
    /// </summary>
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

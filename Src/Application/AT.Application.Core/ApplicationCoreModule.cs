using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Domain.Core.Common.Events;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Application.Core;

public static class ApplicationCoreModule
{
    public static IServiceCollection AddPurgeExpiredIdempotencyKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddUseCasePipeline();
    }
    //#if (anyFeature)

    public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddUseCasePipeline();
    }
    //#endif

    public static IServiceCollection AddUseCasePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(TracingBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehaviour<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>)));

        return services;
    }

    public static IServiceCollection AddDomainEventConsumer<TEvent, TConsumer>(this IServiceCollection services)
        where TEvent : IDomainEvent
        where TConsumer : class, IDomainEventConsumer<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IDomainEventConsumer<TEvent>, TConsumer>();

        return services;
    }
}

using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Caching;
using AppTemplate.Infrastructure.Core.Common.Saving;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Core.Common.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppTemplate.Infrastructure.Core;

/// <summary>
/// What this project registers. Every adapter behind these calls stays internal: a module composes a
/// mechanism by making the call, never by naming the type — which is what
/// <c>AdapterVisibilityTests.Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule</c>
/// holds, and what lets two modules share one mechanism without either owning it.
/// </summary>
public static class InfrastructureCoreModule
{
    /// <summary>
    /// Registers <see cref="ICacheStore"/> over <c>HybridCache</c>, in process. A deployment that
    /// wants a shared second level registers an <c>IDistributedCache</c> beside this call.
    /// </summary>
    public static IServiceCollection AddCacheStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHybridCache();
        services.AddSingleton<ICacheStore, HybridCacheStore>();

        return services;
    }

    /// <summary>
    /// Registers the mechanisms one <typeparamref name="TContext"/> saves through: the three
    /// interceptors, the domain-event dispatcher, and
    /// <see cref="IContextUnitOfWork{TContext}"/> over that context.
    /// </summary>
    /// <remarks>
    /// The commit boundary is registered under <see cref="IContextUnitOfWork{TContext}"/> and not
    /// under the unnamed <see cref="IUnitOfWork"/>, so two modules calling this do not overwrite
    /// each other: whichever module owns the context a use case should commit through registers
    /// that mapping itself. Attaching the interceptors is a second step,
    /// <see cref="AddCoreSavingInterceptors"/>, called where the context's options are built.
    /// </remarks>
    /// <typeparam name="TContext">The context whose saves these mechanisms govern.</typeparam>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddCoreSaving<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AggregateFlushSaveChangesInterceptor>();
        services.TryAddScoped<AuditingSaveChangesInterceptor>();
        services.TryAddScoped<DomainEventDispatchSaveChangesInterceptor>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<IContextUnitOfWork<TContext>, EfUnitOfWork<TContext>>();

        return services;
    }

    /// <summary>
    /// Attaches the three save interceptors to a context's options, in the order they must run:
    /// aggregates are flushed onto their rows, then the rows are stamped, then the events that
    /// flush raised are drained.
    /// </summary>
    /// <param name="options">The context's options, as they are being built.</param>
    /// <param name="provider">The scope the interceptors are resolved from.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    public static DbContextOptionsBuilder AddCoreSavingInterceptors(
        this DbContextOptionsBuilder options,
        IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        return options.AddInterceptors(
            provider.GetRequiredService<AggregateFlushSaveChangesInterceptor>(),
            provider.GetRequiredService<AuditingSaveChangesInterceptor>(),
            provider.GetRequiredService<DomainEventDispatchSaveChangesInterceptor>());
    }

    /// <summary>Registers <see cref="IDateTimeProvider"/> over the system clock.</summary>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddSystemClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }
}

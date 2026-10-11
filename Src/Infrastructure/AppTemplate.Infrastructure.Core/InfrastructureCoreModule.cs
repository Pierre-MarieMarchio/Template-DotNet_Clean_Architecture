using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Core.Common.Caching;
using AppTemplate.Infrastructure.Core.Common.Saving;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Core.Common.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    /// Registers <see cref="IContextUnitOfWork{TContext}"/>: the commit boundary of one context, and
    /// all a context needs to commit. Its saves are flushed, stamped or published only by the
    /// interceptors its own options attach.
    /// </summary>
    /// <remarks>
    /// The commit boundary is registered under <see cref="IContextUnitOfWork{TContext}"/> and not
    /// under the unnamed <see cref="IUnitOfWork"/>, so two modules calling this do not overwrite
    /// each other: whichever module owns the context a use case should commit through registers
    /// that mapping itself.
    /// </remarks>
    /// <typeparam name="TContext">The context whose staged changes it commits.</typeparam>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddContextUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IContextUnitOfWork<TContext>, EfUnitOfWork<TContext>>();

        return services;
    }

    /// <summary>
    /// Registers the three save interceptors and the domain-event dispatcher, so that a context's
    /// options can attach the ones its records need: <see cref="UseAggregateFlushing"/>,
    /// <see cref="UseAuditStamping"/> and <see cref="UseDomainEventDispatch"/>. Registering them
    /// attaches nothing.
    /// </summary>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddSavingInterceptors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AggregateFlushSaveChangesInterceptor>();
        services.TryAddScoped<AuditingSaveChangesInterceptor>();
        services.TryAddScoped<DomainEventDispatchSaveChangesInterceptor>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddScoped<IDomainEventPublisher, DomainEventPublisher>();

        return services;
    }

    /// <summary>
    /// Attaches <see cref="AggregateFlushSaveChangesInterceptor"/>: before a save, every aggregate
    /// staged on the context being saved is written onto its rows. It must be attached before the
    /// other two, which act on what it wrote.
    /// </summary>
    /// <param name="options">The context's options, as they are being built.</param>
    /// <param name="provider">The scope the interceptor is resolved from.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    public static DbContextOptionsBuilder UseAggregateFlushing(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<AggregateFlushSaveChangesInterceptor>(options, provider);

    /// <summary>
    /// Attaches <see cref="AuditingSaveChangesInterceptor"/>, which stamps every added or modified
    /// <see cref="AppTemplate.Domain.Core.Common.Abstractions.IAuditable"/> row with the scope's
    /// actor and clock. A context with no such row has no reason to resolve either.
    /// </summary>
    /// <param name="options">The context's options, as they are being built.</param>
    /// <param name="provider">The scope the interceptor is resolved from.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    public static DbContextOptionsBuilder UseAuditStamping(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<AuditingSaveChangesInterceptor>(options, provider);

    /// <summary>
    /// Attaches <see cref="DomainEventDispatchSaveChangesInterceptor"/>, which collects the events of
    /// the aggregates staged on the context being saved and publishes them once that save has
    /// committed. Attached last, so it collects what the flush raised.
    /// </summary>
    /// <param name="options">The context's options, as they are being built.</param>
    /// <param name="provider">The scope the interceptor is resolved from.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    public static DbContextOptionsBuilder UseDomainEventDispatch(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<DomainEventDispatchSaveChangesInterceptor>(options, provider);

    /// <summary>Registers <see cref="IDateTimeProvider"/> over the system clock.</summary>
    /// <param name="services">The container to register into.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddSystemClock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }

    private static DbContextOptionsBuilder Attach<TInterceptor>(
        DbContextOptionsBuilder options,
        IServiceProvider provider)
        where TInterceptor : class, IInterceptor
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        // Appended: the order of the calls is the order EF runs them in.
        return options.AddInterceptors(provider.GetRequiredService<TInterceptor>());
    }
}

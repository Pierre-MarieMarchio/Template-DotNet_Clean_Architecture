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

public static class InfrastructureCoreModule
{
    public static IServiceCollection AddCacheStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHybridCache();
        services.AddSingleton<ICacheStore, HybridCacheStore>();

        return services;
    }

    public static IServiceCollection AddContextUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IContextUnitOfWork<TContext>, EfUnitOfWork<TContext>>();

        return services;
    }

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

    public static DbContextOptionsBuilder UseAggregateFlushing(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<AggregateFlushSaveChangesInterceptor>(options, provider);

    public static DbContextOptionsBuilder UseAuditStamping(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<AuditingSaveChangesInterceptor>(options, provider);

    public static DbContextOptionsBuilder UseDomainEventDispatch(
        this DbContextOptionsBuilder options,
        IServiceProvider provider) =>
        Attach<DomainEventDispatchSaveChangesInterceptor>(options, provider);

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

        return options.AddInterceptors(provider.GetRequiredService<TInterceptor>());
    }
}

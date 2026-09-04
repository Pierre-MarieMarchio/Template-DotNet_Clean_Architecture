using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.Ports;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.Stores;
using AppTemplate.Infrastructure.Persistence.Common.Auditing;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Mapping;
using AppTemplate.Infrastructure.Persistence.Common.Time;
using AppTemplate.Infrastructure.Persistence.Common.UnitOfWork;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mappers;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence;

/// <summary>
/// Composes all persistence: the one context, the interceptor pipeline, the clock, the unit of work, and
/// each feature's mapper, tracker, repository, queries and stores.
/// <para>
/// Every registration is explicit and named. What this replaces enumerated two assemblies and paired
/// interfaces with implementations by matching type names, so a rename produced a container that started
/// fine and threw on first use.
/// </para>
/// </summary>
public static class PersistenceModule
{
    /// <summary>
    /// Registers everything that touches the database.
    /// <para>
    /// Idempotent, and deliberately so. It is called once from the host for visible ordering, and again
    /// by the identity module — which needs the context and the clock, and says so rather than depending
    /// on the host having composed it first.
    /// </para>
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Default</c> connection string.</param>
    public static IServiceCollection AddPersistenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Guard rather than TryAdd everywhere: TryAdd would cover the services below, but AddDbContext
        // has no Try- form and would happily register a second context and a second options object. One
        // check at the top is both cheaper and easier to reason about than a rule per registration.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(AppDbContext)))
        {
            return services;
        }

        // Fail here, at composition time, rather than on the first request that needs a database.
        string connectionString = DefaultConnectionString.Require(configuration);

        AddSeedingOptions(services, configuration);
        AddSharedServices(services);
        AddTodoListsFeature(services);
        AddIdentityFeature(services);
        AddContext(services, connectionString);

        return services;
    }


    private static void AddSeedingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentitySeedOptions>, IdentitySeedOptionsValidator>();
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // Scoped, because auditing needs the caller of the current request, the flush pipeline needs the
        // aggregates loaded in it, and event dispatch accumulates the events of the current save.
        services.TryAddScoped<AggregateFlushSaveChangesInterceptor>();
        services.TryAddScoped<AuditingSaveChangesInterceptor>();
        services.TryAddScoped<DomainEventDispatchSaveChangesInterceptor>();
        services.TryAddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();
    }

    private static void AddTodoListsFeature(IServiceCollection services)
    {
        // Stateless and EF-free, so one instance serves the whole process.
        services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

        // One tracker per request, resolvable under three contracts because three callers need three
        // different views of it: the repository uses the identity map, the flush interceptor uses the
        // flusher, and the dispatch interceptor uses the event source. Registered through a factory
        // rather than three times over, because three registrations would mean three instances — and
        // then the repository would fill one identity map while the interceptor flushed a different,
        // empty one, and every write would silently do nothing.
        services.TryAddScoped<TodoListTracker>();
        services.TryAddScoped<ITodoListTracker>(provider => provider.GetRequiredService<TodoListTracker>());
        services.AddScoped<IAggregateFlusher>(provider => provider.GetRequiredService<TodoListTracker>());
        services.AddScoped<IDomainEventSource>(provider => provider.GetRequiredService<TodoListTracker>());

        services.TryAddScoped<ITodoListRepository, TodoListRepository>();
        services.TryAddScoped<ITodoListQueries, TodoListQueries>();
    }

    private static void AddIdentityFeature(IServiceCollection services)
    {
        services.TryAddScoped<IRefreshTokenStore, RefreshTokenStore>();

        // Constructible only once the identity module has composed ASP.NET Identity, because seeding an
        // account means hashing a password and generating a security stamp — not writing a row. That is
        // the one dependency this assembly has on another module's registrations, and it is why the
        // container test composes the host as a whole rather than this module alone.
        services.TryAddScoped<IIdentitySeeder, IdentitySeeder>();
    }

    private static void AddContext(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
            options
                .UseNpgsql(connectionString, npgsql => npgsql
                    // Transient connection failures are the driver's problem to solve. The previous
                    // version hand-rolled a retry loop around startup with an empty catch block, which
                    // swallowed configuration errors as if they were network blips and then failed much
                    // later, somewhere unrelated.
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null)
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(
                        AppDbContext.MigrationsHistoryTableName,
                        AppDbContext.MigrationsHistorySchema))

                // The order of these three is load-bearing, which is why they are added here in one place
                // rather than wherever was convenient. Flushing comes first: it is what writes an
                // aggregate's state onto its rows, so nothing downstream can see a change until it has
                // run. Audit stamping comes second, because it only acts on an entry that is already
                // Added or Modified — and the flush is what makes a root Modified when only a child
                // changed. Event dispatch comes last, because it publishes after the commit the other two
                // prepared.
                .AddInterceptors(
                    serviceProvider.GetRequiredService<AggregateFlushSaveChangesInterceptor>(),
                    serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>(),
                    serviceProvider.GetRequiredService<DomainEventDispatchSaveChangesInterceptor>()));
    }
}

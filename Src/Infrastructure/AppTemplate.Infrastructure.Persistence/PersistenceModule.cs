using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Idempotency;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderTargets;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Auditing;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Common.Mapping;
using AppTemplate.Infrastructure.Persistence.Common.Observability;
using AppTemplate.Infrastructure.Persistence.Common.Time;
using AppTemplate.Infrastructure.Persistence.Common.UnitOfWork;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Stores;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence;

/// <summary>
/// Composes all persistence: the one context, the interceptor pipeline, the clock, the unit of work, and
/// each feature's mapper, tracker, repository, queries and stores.
/// <para>
/// Every registration is explicit and named, each interface paired by hand with its implementation, so a
/// rename fails the build instead of producing a container that starts fine and throws on first use.
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
        AddDatabaseOptions(services, configuration);
        AddSharedServices(services);
        AddTodoListsFeature(services);
        AddRemindersFeature(services);
        AddIdentityFeature(services);
        AddIdempotencyFeature(services, configuration);
        AddContext(services, connectionString);
        AddContextFactory(services, connectionString);

        return services;
    }


    private static void AddSeedingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentitySeedOptions>, IdentitySeedOptionsValidator>();
    }

    private static void AddDatabaseOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
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

    private static void AddRemindersFeature(IServiceCollection services)
    {
        services.TryAddSingleton<IReminderMapper, ReminderMapper>();

        // See AddTodoListsFeature for why this is a factory over one scoped instance rather than three
        // separate registrations: the repository, the flush interceptor and the dispatch interceptor
        // must all resolve the very same tracker, or each would hold its own empty identity map.
        services.TryAddScoped<ReminderTracker>();
        services.TryAddScoped<IReminderTracker>(provider => provider.GetRequiredService<ReminderTracker>());
        services.AddScoped<IAggregateFlusher>(provider => provider.GetRequiredService<ReminderTracker>());
        services.AddScoped<IDomainEventSource>(provider => provider.GetRequiredService<ReminderTracker>());

        services.TryAddScoped<IReminderRepository, ReminderRepository>();
        services.TryAddScoped<IReminderTargets, ReminderTargets>();

        // Wraps a static Meter/Counter pair (see ReminderDiagnostics), so one instance per process
        // is a choice made for clarity, not a requirement: a scoped registration would have been
        // just as correct.
        services.TryAddSingleton<IReminderDiagnostics, ReminderDiagnostics>();
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

    private static void AddIdempotencyFeature(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyPurgeOptions>()
            .Bind(configuration.GetSection(IdempotencyPurgeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdempotencyPurgeOptions>, IdempotencyPurgeOptionsValidator>();

        // Scoped: the factory itself is stateless, but IDbContextFactory<T> is conventionally scoped
        // alongside the context it stands beside, and nothing here needs it to outlive a request.
        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();
    }

    private static void AddContext(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options
                .UseNpgsql(WithMaxPoolSize(connectionString, database.MaxPoolSize), npgsql => npgsql
                    // Transient connection failures are the driver's problem to solve. The previous
                    // version hand-rolled a retry loop around startup with an empty catch block, which
                    // swallowed configuration errors as if they were network blips and then failed much
                    // later, somewhere unrelated.
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null)
                    .CommandTimeout(database.CommandTimeoutSeconds)
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
                    serviceProvider.GetRequiredService<DomainEventDispatchSaveChangesInterceptor>());
        });
    }

    /// <summary>
    /// Applies <see cref="DatabaseOptions.MaxPoolSize"/> to the connection string rather than
    /// appending it as text at the call site, so a value that is already present (a deployment that
    /// sets it directly in <c>ConnectionStrings:Default</c>) is overridden consistently instead of
    /// producing a string with the key twice.
    /// </summary>
    private static string WithMaxPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    /// <summary>
    /// A second way to create an <see cref="AppDbContext"/>, for <see cref="IdempotencyStore"/>
    /// alone: a fresh instance — its own change tracker, its own connection, its own commit — rather
    /// than the ambient request-scoped one <c>IUnitOfWork</c> owns.
    /// <para>
    /// <b>Why this registers no options of its own.</b> <c>AddDbContextFactory</c> adds
    /// <c>DbContextOptions&lt;AppDbContext&gt;</c> with <c>TryAdd</c>, and <see cref="AddContext"/>
    /// has already registered one, scoped, with the flush/audit/dispatch interceptors attached. The
    /// options action below is therefore never invoked — it exists only so the call reads as
    /// intentional rather than accidentally relying on that ordering. What actually matters is the
    /// factory's own lifetime: requesting the default (<c>Singleton</c>) here would make it depend on
    /// that scoped options object, which <c>BuildServiceProvider(validateScopes: true)</c> refuses
    /// outright — the exact conflict the task that added this file called out. Registering the
    /// factory <c>Scoped</c> instead makes it consume the same scoped options safely, which also
    /// means every context it creates carries the same interceptor pipeline as the ambient one; that
    /// is harmless here, since none of those interceptors recognise <c>IdempotencyRecord</c> as an
    /// aggregate root or an auditable entity, and it is one fewer configuration to keep in sync with
    /// <see cref="AddContext"/>. It is also why <see cref="DatabaseOptions.MaxPoolSize"/> and
    /// <see cref="DatabaseOptions.CommandTimeoutSeconds"/> need no wiring here: every context this
    /// factory creates carries <see cref="AddContext"/>'s options, pool limit included, so
    /// <see cref="IdempotencyStore"/>'s extra connections count against the same bound as the
    /// ambient one rather than a second pool with a bound of its own.
    /// </para>
    /// </summary>
    private static void AddContextFactory(IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseNpgsql(connectionString),
            lifetime: ServiceLifetime.Scoped);
    }
}

using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
//#if (externalAuth)
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityQueries;
using AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;
using AppTemplate.Application.Features.ExternalIdentity.Ports.RoleAssignmentQueries;
//#endif
//#if (files)
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
//#endif
//#if (notes)
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
//#endif
//#if (todoLists)
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
//#endif
//#if (files)
using AppTemplate.Domain.Features.Files.Repositories;
//#endif
//#if (reminders)
using AppTemplate.Domain.Features.Reminders.Repositories;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Repositories;
//#endif
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving;
//#if (canAddFeature)
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
//#endif
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Common.Leases;
//#if (externalAuth)
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Queries;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Stores;
//#endif
//#if (files)
using AppTemplate.Infrastructure.Persistence.Features.Files.Checkpoints;
using AppTemplate.Infrastructure.Persistence.Features.Files.Locks;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Files.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;
//#endif
//#if (notes)
using AppTemplate.Infrastructure.Persistence.Features.Notes.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Stores;
//#endif
//#if (reminders)
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Observability;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Tracking;
//#endif
//#if (todoLists)
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Queries;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;
//#endif
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
//#if (files)
using Microsoft.Extensions.Logging;
//#endif
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Persistence;

/// <summary>
/// Composes all persistence: the one context, the interceptor pipeline, the clock, the unit of work,
/// and each feature's mapper, tracker, repository, queries and stores.
/// </summary>
public static class PersistenceModule
{
    //#if (reminders)
    /// <summary>
    /// The name of the meter the reminder diagnostics publish on. A host that fires reminders adds
    /// it to its telemetry registration by this constant, so the declaration and the registration
    /// cannot drift apart.
    /// </summary>
    public const string RemindersMeterName = "AppTemplate.Reminders";

    //#endif
    /// <summary>
    /// Registers everything that touches the database. Idempotent: the host calls it, and so does the
    /// authentication module, which needs the context and the clock.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Default</c> connection string.</param>
    public static IServiceCollection AddPersistenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (AlreadyComposed(services))
        {
            return services;
        }

        string connectionString = DefaultConnectionString.Require(configuration);

        AddDatabaseOptions(services, configuration);
        AddSharedServices(services);
        //#if (todoLists)
        AddTodoListsFeature(services);
        //#endif
        //#if (reminders)
        AddRemindersFeature(services);
        //#endif
        //#if (files)
        AddFilesFeature(services, connectionString);
        //#endif
        //#if (notes)
        AddNotesFeature(services);
        //#endif
        AddIdempotencyFeature(services, configuration);
        AddContext(services, connectionString);
        AddContextFactory(services);

        return services;
    }
    //#if (externalAuth)

    /// <summary>
    /// Registers the stores behind the external-identity mapping: the subject-to-owner store, the
    /// profile reads by owner and the local role assignments. Separate from
    /// <see cref="AddPersistenceModule"/> because only a host that authenticates callers with an
    /// external provider has a use for them; it composes the module itself, which is idempotent.
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Default</c> connection string.</param>
    public static IServiceCollection AddExternalIdentityPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistenceModule(configuration);

        services.TryAddScoped<IExternalIdentityStore, ExternalIdentityStore>();
        services.TryAddScoped<IExternalIdentityQueries, ExternalIdentityQueries>();
        services.TryAddScoped<IRoleAssignmentQueries, RoleAssignmentQueries>();

        return services;
    }
    //#endif

    /// <summary>
    /// <c>AddDbContext</c> has no <c>Try</c> form, so a second call would register a second context
    /// and a second options object.
    /// </summary>
    private static bool AlreadyComposed(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(AppDbContext));

    //#if (canAddFeature)
    /// <summary>
    /// One tracker per request, resolved under every contract that needs it: the repository takes the
    /// identity map, the flush interceptor the flusher, the dispatch interceptor the event source.
    /// Registering the three separately would give each its own instance and its own empty identity
    /// map, and every write would silently do nothing.
    /// </summary>
    private static void AddScopedTracker<TTracker, TTrackerPort>(IServiceCollection services)
        where TTracker : class, TTrackerPort, IAggregateFlusher, IDomainEventSource
        where TTrackerPort : class
    {
        services.TryAddScoped<TTracker>();
        services.TryAddScoped<TTrackerPort>(provider => provider.GetRequiredService<TTracker>());
        services.AddScoped<IAggregateFlusher>(provider => provider.GetRequiredService<TTracker>());
        services.AddScoped<IDomainEventSource>(provider => provider.GetRequiredService<TTracker>());
    }

    //#endif
    private static void AddDatabaseOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
    }

    private static void AddSharedServices(IServiceCollection services)
    {
        services.AddSystemClock();

        // Singleton because the background services that take it are singletons, and ValidateScopes
        // refuses a scoped dependency captured by one.
        services.TryAddSingleton<ILeaderLease, PostgresLeaderLease>();

        services.AddContextUnitOfWork<AppDbContext>();
        services.AddSavingInterceptors();

        // The unnamed port is registered once per container, by the module owning the context a use
        // case should commit through. A second module mapping it would commit those writes here.
        services.TryAddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<IContextUnitOfWork<AppDbContext>>());
    }

    //#if (todoLists)
    private static void AddTodoListsFeature(IServiceCollection services)
    {
        services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

        AddScopedTracker<TodoListTracker, ITodoListTracker>(services);

        services.TryAddScoped<ITodoListRepository, TodoListRepository>();
        services.TryAddScoped<ITodoListQueries, TodoListQueries>();
        services.TryAddScoped<ITodoItemQueries, TodoItemQueries>();
        services.TryAddScoped<ITodoItemTagQueries, TodoItemTagQueries>();
    }

    //#endif
    //#if (files)
    private static void AddFilesFeature(IServiceCollection services, string connectionString)
    {
        services.TryAddSingleton<IOwnerQuotaLock>(provider => new PostgresOwnerQuotaLock(
            connectionString,
            provider.GetRequiredService<ILogger<PostgresOwnerQuotaLock>>()));

        services.TryAddSingleton<IStoredFileMapper, StoredFileMapper>();

        AddScopedTracker<StoredFileTracker, IStoredFileTracker>(services);

        services.TryAddScoped<IStoredFileRepository, StoredFileRepository>();
        services.TryAddScoped<IStoredFileQueries, StoredFileQueries>();
        services.TryAddScoped<IStoredFileTagQueries, StoredFileTagQueries>();
        services.TryAddScoped<IOrphanSweepCheckpoint, OrphanSweepCheckpoint>();
    }

    //#endif
    //#if (notes)
    private static void AddNotesFeature(IServiceCollection services)
    {
        // A store and its queries, and nothing else: notes have no mapper, tracker or event source,
        // and commit through the context's unit of work like every other write.
        services.TryAddScoped<INoteStore, NoteStore>();
        services.TryAddScoped<INoteQueries, NoteQueries>();
    }

    //#endif
    //#if (reminders)
    private static void AddRemindersFeature(IServiceCollection services)
    {
        services.TryAddSingleton<IReminderMapper, ReminderMapper>();

        AddScopedTracker<ReminderTracker, IReminderTracker>(services);

        services.TryAddScoped<IReminderRepository, ReminderRepository>();
        services.TryAddScoped<IReminderQueries, ReminderQueries>();
        services.TryAddSingleton<IReminderDiagnostics, ReminderDiagnostics>();
    }

    //#endif
    private static void AddIdempotencyFeature(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyPurgeOptions>()
            .Bind(configuration.GetSection(IdempotencyPurgeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdempotencyPurgeOptions>, IdempotencyPurgeOptionsValidator>();

        services.TryAddScoped<IIdempotencyStore, IdempotencyStore>();
    }

    private static void AddContext(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options
                .UseNpgsql(WithMaxPoolSize(connectionString, database.MaxPoolSize), npgsql => npgsql
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null)
                    .CommandTimeout(database.CommandTimeoutSeconds)
                    .MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(
                        AppDbContext.MigrationsHistoryTableName,
                        AppDbContext.MigrationsHistorySchema))

                // In this order: aggregates are written onto their rows, the rows are stamped, then
                // the events the flush raised are collected, to be published after the commit.
                .UseAggregateFlushing(serviceProvider)
                .UseAuditStamping(serviceProvider)
                .UseDomainEventDispatch(serviceProvider);
        });
    }

    /// <summary>
    /// Overrides <see cref="DatabaseOptions.MaxPoolSize"/> in the connection string, so a deployment
    /// that sets it in <c>ConnectionStrings:Default</c> does not end up with the key twice.
    /// </summary>
    private static string WithMaxPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    /// <summary>
    /// A second way to create an <see cref="AppDbContext"/>, for <see cref="IdempotencyStore"/> and
    /// the orphan sweep's checkpoint: their own change tracker, connection and commit, rather than the
    /// request-scoped one.
    /// <para>
    /// The factory takes no options action of its own, and that is load-bearing. An action here would
    /// be a second configuration layered after <see cref="AddContext"/>'s: it would replace the
    /// connection string built with <see cref="DatabaseOptions.MaxPoolSize"/> by the raw one, and the
    /// factory's contexts would use a second pool. With none, the factory reuses the registered
    /// options, so it gets the same connection string, retry policy and interceptors as the scoped
    /// context.
    /// </para>
    /// <para>
    /// <c>Scoped</c> is load-bearing too: the default <c>Singleton</c> lifetime would make the factory
    /// depend on those scoped options, which <c>BuildServiceProvider(validateScopes: true)</c> refuses.
    /// </para>
    /// </summary>
    private static void AddContextFactory(IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>(lifetime: ServiceLifetime.Scoped);
    }
}

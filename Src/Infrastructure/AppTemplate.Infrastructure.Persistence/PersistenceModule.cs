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
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Access.Queries;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Access.Stores;
//#endif
//#if (files)
using AppTemplate.Infrastructure.Persistence.Features.Files.Access.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Files.Access.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Checkpoints;
using AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Tracking;
//#endif
//#if (notes)
using AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Access.Stores;
//#endif
//#if (reminders)
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Access.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Access.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Adapters.Observability;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Tracking;
//#endif
//#if (todoLists)
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Queries;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Access.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;
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

public static class PersistenceModule
{
    //#if (reminders)
    public const string RemindersMeterName = "AppTemplate.Reminders";

    //#endif
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

    private static bool AlreadyComposed(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(AppDbContext));

    //#if (canAddFeature)
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

        services.TryAddSingleton<ILeaderLease, PostgresLeaderLease>();

        services.AddContextUnitOfWork<AppDbContext>();
        services.AddSavingInterceptors();

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

                .UseAggregateFlushing(serviceProvider)
                .UseAuditStamping(serviceProvider)
                .UseDomainEventDispatch(serviceProvider);
        });
    }

    private static string WithMaxPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    private static void AddContextFactory(IServiceCollection services)
    {
        services.AddDbContextFactory<AppDbContext>(lifetime: ServiceLifetime.Scoped);
    }
}

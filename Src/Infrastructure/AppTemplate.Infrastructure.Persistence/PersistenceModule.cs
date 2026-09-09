using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Ports.StoredFileTagQueries;
using AppTemplate.Application.Features.Reminders.Ports.ReminderDiagnostics;
using AppTemplate.Application.Features.Reminders.Ports.ReminderTargetQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Common.Leases;
using AppTemplate.Infrastructure.Persistence.Features.Files.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Files.Queries;
using AppTemplate.Infrastructure.Persistence.Features.Files.Repositories;
using AppTemplate.Infrastructure.Persistence.Features.Files.Tracking;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Observability;
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
/// Composes all persistence: the one context, the interceptor pipeline, the clock, the unit of work,
/// and each feature's mapper, tracker, repository, queries and stores.
/// </summary>
public static class PersistenceModule
{
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
        AddTodoListsFeature(services);
        AddRemindersFeature(services);
        AddFilesFeature(services);
        AddIdempotencyFeature(services, configuration);
        AddContext(services, connectionString);
        AddContextFactory(services, connectionString);

        return services;
    }


    /// <summary>
    /// <c>AddDbContext</c> has no <c>Try</c> form, so a second call would register a second context
    /// and a second options object.
    /// </summary>
    private static bool AlreadyComposed(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(AppDbContext));

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

        services.AddCoreSaving<AppDbContext>();

        // The unnamed port is registered once per container, by the module owning the context a use
        // case should commit through. A second module mapping it would commit those writes here.
        services.TryAddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<IContextUnitOfWork<AppDbContext>>());
    }

    private static void AddTodoListsFeature(IServiceCollection services)
    {
        services.TryAddSingleton<ITodoListMapper, TodoListMapper>();

        AddScopedTracker<TodoListTracker, ITodoListTracker>(services);

        services.TryAddScoped<ITodoListRepository, TodoListRepository>();
        services.TryAddScoped<ITodoListQueries, TodoListQueries>();
        services.TryAddScoped<ITodoItemTagQueries, TodoItemTagQueries>();
    }

    private static void AddFilesFeature(IServiceCollection services)
    {
        services.TryAddSingleton<IStoredFileMapper, StoredFileMapper>();

        AddScopedTracker<StoredFileTracker, IStoredFileTracker>(services);

        services.TryAddScoped<IStoredFileRepository, StoredFileRepository>();
        services.TryAddScoped<IStoredFileQueries, StoredFileQueries>();
        services.TryAddScoped<IStoredFileTagQueries, StoredFileTagQueries>();
    }

    private static void AddRemindersFeature(IServiceCollection services)
    {
        services.TryAddSingleton<IReminderMapper, ReminderMapper>();

        AddScopedTracker<ReminderTracker, IReminderTracker>(services);

        services.TryAddScoped<IReminderRepository, ReminderRepository>();
        services.TryAddScoped<IReminderTargetQueries, ReminderTargetQueries>();
        services.TryAddSingleton<IReminderDiagnostics, ReminderDiagnostics>();
    }

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

                .AddCoreSavingInterceptors(serviceProvider);
        });
    }

    /// <summary>
    /// Overrides <see cref="DatabaseOptions.MaxPoolSize"/> in the connection string, so a deployment
    /// that sets it in <c>ConnectionStrings:Default</c> does not end up with the key twice.
    /// </summary>
    private static string WithMaxPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

    /// <summary>
    /// A second way to create an <see cref="AppDbContext"/>, for <see cref="IdempotencyStore"/>
    /// alone: its own change tracker, connection and commit, rather than the request-scoped one.
    /// <para>
    /// <c>Scoped</c> is load-bearing. <c>AddDbContextFactory</c> registers the options with
    /// <c>TryAdd</c>, so <see cref="AddContext"/>'s scoped options win and the action below is never
    /// invoked; the default <c>Singleton</c> lifetime would make the factory depend on those scoped
    /// options, which <c>BuildServiceProvider(validateScopes: true)</c> refuses.
    /// </para>
    /// </summary>
    private static void AddContextFactory(IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<AppDbContext>(
            options => options.UseNpgsql(connectionString),
            lifetime: ServiceLifetime.Scoped);
    }
}

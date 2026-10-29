using System.Globalization;
using System.Security.Claims;
//#if (postgreSql)
using System.Text.RegularExpressions;
//#endif
using AppTemplate.Api.Common.Security;
//#if (todoLists || notes)
using AppTemplate.Application;
//#endif
//#if (todoLists)
using AppTemplate.Application.Core;
using AppTemplate.Application.Core.Common.Events;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Model.Events;
//#endif
//#if (localAuth)
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
//#endif
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.UnitTests.Composition;

internal sealed partial class SqliteSavingHost : IAsyncDisposable
{
    private readonly SqliteConnection _businessKeeper;
    //#if (localAuth)
    private readonly SqliteConnection _authKeeper;
    //#endif
    private readonly HttpContextAccessor _httpContextAccessor;

    private SqliteSavingHost(
        SqliteConnection businessKeeper,
        //#if (localAuth)
        SqliteConnection authKeeper,
        //#endif
        HttpContextAccessor httpContextAccessor,
        ServiceProvider services,
        PublishedEvents published)
    {
        _businessKeeper = businessKeeper;
        //#if (localAuth)
        _authKeeper = authKeeper;
        //#endif
        _httpContextAccessor = httpContextAccessor;
        Services = services;
        Published = published;
    }

    public ServiceProvider Services { get; }

    public PublishedEvents Published { get; }

    public static async Task<SqliteSavingHost> StartAsync(CancellationToken cancellationToken)
    {
        string businessDatabase = AnInMemoryDatabase("business");
        //#if (localAuth)
        string authDatabase = AnInMemoryDatabase("auth");
        //#endif

        var businessKeeper = new SqliteConnection(businessDatabase);
        //#if (localAuth)
        var authKeeper = new SqliteConnection(authDatabase);
        //#endif
        await businessKeeper.OpenAsync(cancellationToken);
        //#if (localAuth)
        await authKeeper.OpenAsync(cancellationToken);
        //#endif

        var httpContextAccessor = new HttpContextAccessor();
        var published = new PublishedEvents(businessDatabase);

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddApiCallerIdentity();

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        services.AddPersistenceModule(Configuration());
        //#if (localAuth)
        services.AddAuthModule(Configuration());
        //#endif

        services.AddSingleton(published);
        //#if (todoLists)
        services.AddDomainEventConsumer<TodoListCreatedDomainEvent, RecordingCreatedConsumer>();
        services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, RecordingCompletedConsumer>();
        //#endif

        OnSqlite<AppDbContext>(services, businessDatabase);
        //#if (localAuth)
        OnSqlite<AuthDbContext>(services, authDatabase);
        //#endif

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        /*#if (localAuth)*/
        var host = new SqliteSavingHost(businessKeeper, authKeeper, httpContextAccessor, provider, published);
        /*#else
        var host = new SqliteSavingHost(businessKeeper, httpContextAccessor, provider, published);
        #endif*/

        await host.CreateSchemaAsync<AppDbContext>(businessKeeper, cancellationToken);
        //#if (localAuth)
        await host.CreateSchemaAsync<AuthDbContext>(authKeeper, cancellationToken);
        //#endif

        return host;
    }

    public AsyncServiceScope RequestScope(Guid? userId = null)
    {
        var context = new DefaultHttpContext();

        if (userId is { } id)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())],
                authenticationType: "Test"));
        }

        _httpContextAccessor.HttpContext = context;

        return Services.CreateAsyncScope();
    }

    public AsyncServiceScope BackgroundScope() =>
        Services.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

    public Task<int> ExecuteOnBusinessStoreAsync(string sql, CancellationToken cancellationToken) =>
        Published.ExecuteAsync(sql, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _httpContextAccessor.HttpContext = null;
        await Services.DisposeAsync();
        await _businessKeeper.DisposeAsync();
        //#if (localAuth)
        await _authKeeper.DisposeAsync();
        //#endif
    }

    internal static string AnInMemoryDatabase(string name) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = string.Create(CultureInfo.InvariantCulture, $"{name}-{Guid.NewGuid():N}"),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

    internal static void OnSqlite<TContext>(IServiceCollection services, string connectionString)
        where TContext : DbContext
    {
        var composed = services.Single(
            descriptor => descriptor.ServiceType == typeof(DbContextOptions<TContext>));
        var compose = composed.ImplementationFactory
            ?? throw new InvalidOperationException(
                $"The options for {typeof(TContext).Name} are no longer registered as a factory.");

        services.Remove(composed);
        services.Add(new ServiceDescriptor(
            typeof(DbContextOptions<TContext>),
            provider =>
            {
                var options = (DbContextOptions<TContext>)compose(provider);
                var withoutProvider = options.Extensions
                    .Where(extension => extension is not RelationalOptionsExtension)
                    .ToDictionary(extension => extension.GetType());

                return new DbContextOptionsBuilder<TContext>(new DbContextOptions<TContext>(withoutProvider))
                    .UseSqlite(connectionString)
                    .Options;
            },
            composed.Lifetime));
    }

    private Task CreateSchemaAsync<TContext>(
        SqliteConnection connection,
        CancellationToken cancellationToken)
        where TContext : DbContext =>
        CreateSchemaAsync<TContext>(Services, connection, cancellationToken);

    internal static async Task CreateSchemaAsync<TContext>(
        IServiceProvider services,
        SqliteConnection connection,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        string script = scope.ServiceProvider.GetRequiredService<TContext>().Database.GenerateCreateScript();

        await using var command = connection.CreateCommand();
        /*#if (postgreSql)*/
        command.CommandText = XminColumn().Replace(script, "\"xmin\" INTEGER NOT NULL DEFAULT 1");
        /*#else
        command.CommandText = script;
        #endif*/
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = DatabaseProvider.DesignTimeConnectionString,
            })
            .Build();

    //#if (postgreSql)
    [GeneratedRegex("\"xmin\" xid NOT NULL")]
    private static partial Regex XminColumn();
    //#endif

    //#if (todoLists)
    private sealed class RecordingCreatedConsumer(PublishedEvents published)
        : IDomainEventConsumer<TodoListCreatedDomainEvent>
    {
        public Task ConsumeAsync(
            TodoListCreatedDomainEvent domainEvent,
            CancellationToken cancellationToken = default) =>
            published.RecordAsync(domainEvent, domainEvent.TodoListId, cancellationToken);
    }

    private sealed class RecordingCompletedConsumer(PublishedEvents published)
        : IDomainEventConsumer<TodoItemCompletedDomainEvent>
    {
        public Task ConsumeAsync(
            TodoItemCompletedDomainEvent domainEvent,
            CancellationToken cancellationToken = default) =>
            published.RecordAsync(domainEvent, domainEvent.TodoListId, cancellationToken);
    }
    //#endif
}

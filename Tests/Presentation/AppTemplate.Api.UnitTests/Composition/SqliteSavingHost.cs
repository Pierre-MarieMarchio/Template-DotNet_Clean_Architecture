using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Common.Security;
//#if (todoLists || notes)
using AppTemplate.Application;
//#endif
//#if (todoLists)
using AppTemplate.Application.Core;
using AppTemplate.Application.Core.Common.Events;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Events;
//#endif
//#if (localAuth)
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
//#endif
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.UnitTests.Composition;

/// <summary>
/// The host's persistence contexts, composed by the real module entry points, with PostgreSQL
/// swapped for an in-memory SQLite database per context.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is real.</b> <c>AddPersistenceModule</c>, the local accounts' module where the host has
/// one, the application's consumers and the host's caller identity are composed exactly as <c>Program.cs</c> composes them,
/// so each context carries whatever save-changes interceptors its module attaches, resolved from the
/// scope the way the module resolves them. Only the provider extension of each composed
/// <c>DbContextOptions</c> is replaced: everything else the module put on the options, the
/// interceptors included, is kept as built.
/// </para>
/// <para>
/// <b>Why SQLite.</b> A save has to really open a transaction and really commit for "published only
/// after its own commit" to mean anything, and every context instance has to own its connection the
/// way a pooled Npgsql one does. A shared-cache in-memory database gives both without a container
/// runtime; EF's in-memory provider has no transaction to commit. The one PostgreSQL feature the
/// model needs, the <c>xmin</c> token, becomes a plain integer column defaulting to 1: enough for EF
/// to read a token back and put it in a <c>WHERE</c> clause, and for a test to move it.
/// </para>
/// </remarks>
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

    /// <summary>Every event a consumer received, with what the business store held at that moment.</summary>
    public PublishedEvents Published { get; }

    public static async Task<SqliteSavingHost> StartAsync(CancellationToken cancellationToken)
    {
        string businessDatabase = AnInMemoryDatabase("business");
        //#if (localAuth)
        string authDatabase = AnInMemoryDatabase("auth");
        //#endif

        // A shared-cache in-memory database lives as long as one connection to it is open.
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

        // Before the caller identity, which only adds an accessor when none is registered.
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

    /// <summary>A request scope whose caller is <paramref name="userId"/>, or anonymous.</summary>
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

    /// <summary>A scope opened the way a background job opens one.</summary>
    public AsyncServiceScope BackgroundScope() =>
        Services.GetRequiredService<IServiceScopeFactory>().CreateBackgroundScope();

    /// <summary>Runs SQL against the business database over a connection no context owns.</summary>
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

    /// <summary>
    /// Keeps the options the module composed and replaces only their provider. Both the scoped
    /// context and the idempotency factory are handed these, as they are handed the module's own.
    /// </summary>
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

    /// <summary>Creates <typeparamref name="TContext"/>'s model in the database behind <paramref name="connection"/>.</summary>
    internal static async Task CreateSchemaAsync<TContext>(
        IServiceProvider services,
        SqliteConnection connection,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        string script = scope.ServiceProvider.GetRequiredService<TContext>().Database.GenerateCreateScript();

        await using var command = connection.CreateCommand();
        command.CommandText = XminColumn().Replace(script, "\"xmin\" INTEGER NOT NULL DEFAULT 1");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                // Read at composition and replaced above; never opened.
                ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            })
            .Build();

    [GeneratedRegex("\"xmin\" xid NOT NULL")]
    private static partial Regex XminColumn();

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

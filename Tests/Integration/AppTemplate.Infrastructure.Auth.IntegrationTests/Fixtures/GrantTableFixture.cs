using System.Data;
using System.Data.Common;
using System.Globalization;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;

public sealed class GrantTableFixture : IAsyncLifetime
{
    private const string _postgresImage =
        "postgres:18.6-alpine3.23@sha256:885cf05d376c7cf27afef02073e6bdac3841252537f16e244fd1c1e6a7c99fb1";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(_postgresImage)
        .WithDatabase("apptemplate_identity_unit_tests")
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    private IConfiguration? _configuration;

    private ServiceProvider? _services;

    public IServiceProvider Services => _services
        ?? throw new InvalidOperationException($"{nameof(GrantTableFixture)} has not been initialised.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),

                ["IdentitySeed:Enabled"] = "false",
            })
            .Build();

        _services = Compose(_configuration);

        await using var scope = _services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await DatabaseReadiness.WaitAsync(context, TestContext.Current.CancellationToken);

        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    public ServiceProvider ComposeWith(params IInterceptor[] interceptors) =>
        Compose(
            _configuration ?? throw new InvalidOperationException($"{nameof(GrantTableFixture)} has not been initialised."),
            services => services.ConfigureDbContext<AuthDbContext>(options => options.AddInterceptors(interceptors)));

    public async Task<AppUser> CreateUserAsync(CancellationToken cancellationToken)
    {
        string suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[..12];
        string userName = $"grant-{suffix}";
        string email = $"{userName}@identity.test";

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture),
            ConcurrencyStamp = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture),
        };

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        return user;
    }

    public async Task<int> CountLiveGrantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();

        var connection = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT count(*) FROM {AuthDbContext.IdentitySchema}."RefreshTokens"
            WHERE "UserId" = @userId AND "RevokedAt" IS NULL
            """;

        command.Parameters.Add(Parameter(command, "userId", userId));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<int> CountLiveSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();

        var connection = scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT count(*) FROM {AuthDbContext.IdentitySchema}."RefreshTokenFamilies" AS f
            WHERE f."UserId" = @userId AND f."RevokedAt" IS NULL
              AND EXISTS (
                SELECT 1 FROM {AuthDbContext.IdentitySchema}."RefreshTokens" AS t
                WHERE t."FamilyId" = f."Id" AND t."RevokedAt" IS NULL)
            """;

        command.Parameters.Add(Parameter(command, "userId", userId));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static ServiceProvider Compose(IConfiguration configuration, Action<IServiceCollection>? adjust = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IAuditActor, AnonymousAuditActor>();
        services.AddAuthModule(configuration);
        adjust?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;

        return parameter;
    }
}

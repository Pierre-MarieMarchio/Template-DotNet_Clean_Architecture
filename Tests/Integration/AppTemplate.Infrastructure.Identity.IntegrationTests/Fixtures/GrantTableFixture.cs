using System.Data;
using System.Data.Common;
using System.Globalization;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;

/// <summary>
/// A real PostgreSQL and the persistence module composed against it, started once for the class that
/// uses it.
/// </summary>
/// <remarks>
/// <para>
/// The grant table is composed exactly as the identity module composes it — through
/// <c>AddPersistenceModule</c> and resolved as <c>IRefreshTokenTable</c> — so what the tests exercise
/// is the registration the host builds, not a hand-wired copy of it.
/// </para>
/// <para>
/// A container rather than an in-memory provider, because the property under test is a property of
/// the database: which of two concurrent conditional updates affects a row. An in-memory or
/// hand-written table would answer whatever it was written to answer.
/// </para>
/// </remarks>
public sealed class GrantTableFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("apptemplate_identity_unit_tests")
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    private ServiceProvider? _services;

    public IServiceProvider Services => _services
        ?? throw new InvalidOperationException($"{nameof(GrantTableFixture)} has not been initialised.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),

                // What IdentitySeedOptionsValidator requires when no password is supplied. Nothing
                // here resolves the seeder, but the section is bound at composition time.
                ["IdentitySeed:Enabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();

        // Logging, because the interceptors take an ILogger; and the current user, because auditing
        // takes one. Both are things the host supplies rather than a module.
        services.AddLogging();
        services.AddSingleton<ICurrentUser, AnonymousCurrentUser>();
        services.AddPersistenceModule(configuration);

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        // CancellationToken.None deliberately: teardown must run even when the run is being
        // cancelled, or the container is left behind.
        await _container.DisposeAsync();
    }

    /// <summary>An account for grants to hang off, since the grant table has a foreign key to it.</summary>
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
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        return user;
    }

    /// <summary>
    /// How many of a user's grants are still live, read over the context's own connection.
    /// </summary>
    /// <remarks>
    /// Raw SQL rather than a <c>DbSet</c>: the grant row type is internal to the persistence
    /// assembly, and a test that could name it could also write one without going through the table.
    /// </remarks>
    public async Task<int> CountLiveGrantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = Services.CreateAsyncScope();

        var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT count(*) FROM {AppDbContext.IdentitySchema}."RefreshTokens"
            WHERE "UserId" = @userId AND "RevokedAt" IS NULL
            """;

        command.Parameters.Add(Parameter(command, "userId", userId));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static DbParameter Parameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;

        return parameter;
    }
}

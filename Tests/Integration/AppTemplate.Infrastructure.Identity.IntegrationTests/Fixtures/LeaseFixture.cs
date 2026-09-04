using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.IntegrationTests.Fixtures;

/// <summary>
/// A real PostgreSQL, and as many independent hosts composed against it as a test asks for.
/// </summary>
/// <remarks>
/// <para>
/// One container of services per lease, because the guarantee is about two <em>hosts</em>. The lease
/// is registered as a singleton, so one container yields one instance, and two participants sharing
/// an instance would be a weaker claim than the one being made.
/// </para>
/// <para>
/// No migrations, unlike <see cref="GrantTableFixture"/>: an advisory lock lives in the server's own
/// lock table and touches no relation, so a schema is a precondition of nothing asserted here.
/// </para>
/// <para>
/// The lease is reached through <see cref="ILeaderLease"/> as the module registers it. The adapter
/// behind it is internal to the persistence assembly and this project holds no
/// <c>InternalsVisibleTo</c> from it, which is the right way round: what the background services
/// depend on is the port, and a test naming the class could compose it in a way no host ever does.
/// </para>
/// </remarks>
public sealed class LeaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("apptemplate_lease_tests")
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    private readonly List<ServiceProvider> _hosts = [];

    public async ValueTask InitializeAsync() =>
        await _container.StartAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// One more host's worth of composition, and the lease it would resolve.
    /// </summary>
    /// <remarks>
    /// Not synchronised, and it does not need to be: xUnit runs the methods of one class one after
    /// another, and every call here happens on the test's own thread before anything races.
    /// </remarks>
    public ILeaderLease CreateLease()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
            })
            .Build();

        var services = new ServiceCollection();

        // Logging, because the adapter takes an ILogger. Configuration, because it takes an
        // IConfiguration *from the container* — the only registration in the persistence module that
        // does, every other one being handed what it needs by AddPersistenceModule itself. A real
        // host registers it, so a fixture that did not would be testing a composition no host has;
        // without it, resolving the lease throws.
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPersistenceModule(configuration);

        var host = services.BuildServiceProvider();
        _hosts.Add(host);

        return host.GetRequiredService<ILeaderLease>();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        // CancellationToken.None deliberately: teardown must run even when the run is being
        // cancelled, or the container is left behind.
        await _container.DisposeAsync();
    }
}

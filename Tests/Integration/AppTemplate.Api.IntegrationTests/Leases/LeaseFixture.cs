using AppTemplate.Application.Core.Common.Ports;
//#if (files)
using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
//#endif
using AppTemplate.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class LeaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(ApiFixture.PostgresImage)
        .WithDatabase("apptemplate_lease_tests")
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    private readonly List<ServiceProvider> _hosts = [];

    public async ValueTask InitializeAsync() =>
        await _container.StartAsync(TestContext.Current.CancellationToken);

    public ILeaderLease CreateLease() => CreateHost().GetRequiredService<ILeaderLease>();

    //#if (files)
    public IOwnerQuotaLock CreateQuotaLock() => CreateHost().GetRequiredService<IOwnerQuotaLock>();

    //#endif
    private ServiceProvider CreateHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
            })
            .Build();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPersistenceModule(configuration);

        var host = services.BuildServiceProvider();
        _hosts.Add(host);

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

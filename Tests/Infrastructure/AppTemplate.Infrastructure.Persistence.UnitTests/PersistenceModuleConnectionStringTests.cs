using System.Globalization;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests;

public sealed class PersistenceModuleConnectionStringTests
{
    private const int _configuredMaxPoolSize = 37;

    [Fact]
    public void ScopedContext_UsesThe_configuredMaxPoolSize()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        using var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        MaxPoolSizeOf(context).ShouldBe(_configuredMaxPoolSize);
    }

    [Fact]
    public void FactoryContext_UsesThe_configuredMaxPoolSize()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var context = factory.CreateDbContext();

        MaxPoolSizeOf(context).ShouldBe(_configuredMaxPoolSize);
    }

    [Fact]
    public void ScopedAndFactoryContexts_ShareOneConnectionString()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        using var scoped = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        using var created = factory.CreateDbContext();

        created.Database.GetConnectionString().ShouldBe(scoped.Database.GetConnectionString());
    }

    private static int MaxPoolSizeOf(DbContext context) =>
        new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()).MaxPoolSize;

    private static ServiceProvider Compose()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = DatabaseProvider.DesignTimeConnectionString,
                ["Database:MaxPoolSize"] = _configuredMaxPoolSize.ToString(CultureInfo.InvariantCulture),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuditActor, NobodyInParticular>();

        return services
            .AddPersistenceModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private sealed class NobodyInParticular : IAuditActor
    {
        public Guid? UserId => null;
    }
}

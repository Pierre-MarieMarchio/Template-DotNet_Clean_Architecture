//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Contexts;
//#endif
using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly DatabaseServer _server = new("apptemplate_integration_tests");

    private ApiFactory? _factory;
    private TestDatabase? _database;

    public ApiFactory Factory => _factory ?? throw NotInitialised();

    public TestDatabase Database => _database ?? throw NotInitialised();

    public FixedDateTimeProvider Clock => Factory.Services.GetRequiredService<FixedDateTimeProvider>();

    public RecordedEmails Emails => Factory.Services.GetRequiredService<RecordedEmails>();

    public CapturedLogs Logs => Factory.Services.GetRequiredService<CapturedLogs>();

    //#if (todoLists)
    public RecordedDomainEvents DomainEvents => Factory.Services.GetRequiredService<RecordedDomainEvents>();
    //#endif

    public async ValueTask InitializeAsync()
    {
        await _server.StartAsync(TestContext.Current.CancellationToken);

        _factory = new ApiFactory(_server.ConnectionString);

        await using var scope = _factory.Services.CreateAsyncScope();
        //#if (localAuth)

        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database
            .MigrateAsync(TestContext.Current.CancellationToken);
        //#endif

        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .MigrateAsync(TestContext.Current.CancellationToken);

        _database = new TestDatabase(_factory.Services);
        await _database.PrepareAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    private static InvalidOperationException NotInitialised() =>
        new($"{nameof(ApiFixture)} has not been initialised.");
}

[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    public const string Name = "AppTemplate.Api integration";
}

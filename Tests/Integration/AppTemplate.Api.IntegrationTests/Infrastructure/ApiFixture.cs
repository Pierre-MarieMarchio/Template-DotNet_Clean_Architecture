using AppTemplate.Infrastructure.InMemory.Common.Email;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The PostgreSQL container and the API host, started once for the whole suite.
/// </summary>
/// <remarks>
/// A collection fixture, because starting a container and booting the host costs seconds and
/// neither depends on anything a test does. What <em>is</em> per-test — the contents of the
/// database, the recorded mailbox, the clock, the captured log — is reset by
/// <see cref="IntegrationTestBase"/>, and every test class belongs to the one collection so that
/// those resets cannot race.
/// </remarks>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("apptemplate_integration_tests")
        .WithUsername("apptemplate_tests")
        .WithPassword("apptemplate_tests")
        .Build();

    private ApiFactory? _factory;
    private TestDatabase? _database;

    public ApiFactory Factory => _factory ?? throw NotInitialised();

    public TestDatabase Database => _database ?? throw NotInitialised();

    /// <summary>The one clock the host reads, resolvable as its concrete type so a test can move it.</summary>
    public FixedDateTimeProvider Clock => Factory.Services.GetRequiredService<FixedDateTimeProvider>();

    public RecordedEmails Emails => Factory.Services.GetRequiredService<RecordedEmails>();

    public CapturedLogs Logs => Factory.Services.GetRequiredService<CapturedLogs>();

    public RecordedDomainEvents DomainEvents => Factory.Services.GetRequiredService<RecordedDomainEvents>();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);

        _factory = new ApiFactory(_container.GetConnectionString());

        // Touching Services builds and starts the host, which is also when the API's own
        // Development bootstrap applies the migrations. They are applied again here, explicitly and
        // idempotently, so the schema is a stated precondition of the suite rather than a side
        // effect of which environment the test host happens to run under.
        await using var scope = _factory.Services.CreateAsyncScope();

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

        // CancellationToken.None deliberately: teardown must run even when the test run is being
        // cancelled, or the container is left behind.
        await _container.DisposeAsync();
    }

    private static InvalidOperationException NotInitialised() =>
        new($"{nameof(ApiFixture)} has not been initialised.");
}

/// <summary>
/// The single collection every test class joins.
/// </summary>
/// <remarks>
/// One collection means one container, and it also means the classes run one after another. That is
/// required, not incidental: the reset between tests truncates every table of every module schema,
/// so two tests running at the same time against the same database would delete each other's rows.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ApiCollectionDefinition : ICollectionFixture<ApiFixture>
{
    public const string Name = "AppTemplate.Api integration";
}

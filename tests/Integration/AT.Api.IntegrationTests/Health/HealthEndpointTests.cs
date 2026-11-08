using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Contexts;
//#endif
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Health;

public sealed class HealthEndpointTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /*#if (localAuth)*/
    private static readonly string[] _registeredChecks = ["auth-database", "database", "shutdown"];
    /*#else
    private static readonly string[] _registeredChecks = ["database", "shutdown"];
    #endif*/

    [Fact]
    public async Task Liveness_IsAnonymousAndHealthy()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Readiness_IsAnonymousAndHealthyAgainstTheRealDatabase()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestToken)).ShouldBe("Healthy");
    }

    [Fact]
    public void Readiness_CoversTheDatabaseAndTheShutdownSignal() =>
        ReadyCheckNames().ShouldBe(_registeredChecks, ignoreOrder: true);

    [Fact]
    public void Liveness_RunsNoChecks()
    {
        Registrations()
            .Select(registration => registration.Name)
            .ShouldBe(_registeredChecks, ignoreOrder: true);
    }

    [Fact]
    public async Task BothFeatureSchemasWereMigrated()
    {
        var schemas = await Database.QueryAsync(DatabaseServer.SchemasSql, TestToken);

        schemas.ShouldContain(AppDbContext.PlatformSchema);
        //#if (localAuth)
        schemas.ShouldContain(AuthDbContext.IdentitySchema);
        //#endif
    }

    [Fact]
    public async Task EachContextHasAMigrationsHistoryOfItsOwn()
    {
        var histories = await Database.QueryAsync(
            DatabaseServer.HistorySchemasSql(AppDbContext.MigrationsHistoryTableName),
            TestToken);

        /*#if (localAuth)*/
        histories.ShouldBe([AuthDbContext.MigrationsHistorySchema, AppDbContext.MigrationsHistorySchema]);
        /*#else
        histories.ShouldBe([AppDbContext.MigrationsHistorySchema]);
        #endif*/
    }

    private IEnumerable<HealthCheckRegistration> Registrations() =>
        Fixture.Factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;

    private List<string> ReadyCheckNames() =>
        [.. Registrations()
            .Where(registration => registration.Tags.Contains("ready"))
            .Select(registration => registration.Name)];
}

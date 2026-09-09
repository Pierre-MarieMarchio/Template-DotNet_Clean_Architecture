using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Health;

/// <summary>
/// Liveness answers without touching a dependency; readiness answers for both databases.
/// </summary>
public sealed class HealthEndpointTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Liveness_IsAnonymousAndHealthy()
    {
        // No Authorization header: the fallback policy requires an authenticated user, so a probe
        // that had to log in would be useless to an orchestrator.
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

    /// <summary>
    /// The readiness endpoint answers in minimal plaintext, so its body names no individual check.
    /// This is what proves what readiness reports on: the two contexts it needs to serve a request, and
    /// whether the host has begun shutting down. Both carry the tag the endpoint filters by.
    /// </summary>
    [Fact]
    public void Readiness_CoversTheDatabaseAndTheShutdownSignal() =>
        // Which checks, not in which order: the predicate selects them by tag, and the order they
        // were registered in is an artefact of the shutdown check arriving with AddCoreHealthChecks
        // and the database check being chained onto the builder it returns.
        ReadyCheckNames().ShouldBe(["auth-database", "database", "shutdown"], ignoreOrder: true);

    /// <summary>
    /// Liveness must have no dependency at all: a database blip should not make an orchestrator
    /// restart a process that is running perfectly well, and neither should a graceful shutdown —
    /// failing liveness during a drain asks the orchestrator to kill a process that is finishing
    /// its work correctly.
    /// </summary>
    [Fact]
    public void Liveness_RunsNoChecks()
    {
        // Every registered check is a readiness check, and the liveness endpoint's predicate
        // excludes all of them.
        Registrations()
            .Select(registration => registration.Name)
            .ShouldBe(["auth-database", "database", "shutdown"], ignoreOrder: true);
    }

    [Fact]
    public async Task BothFeatureSchemasWereMigrated()
    {
        var schemas = await Database.QueryAsync(
            """
            SELECT DISTINCT table_schema
            FROM information_schema.tables
            WHERE table_type = 'BASE TABLE'
            """,
            TestToken);

        schemas.ShouldContain(AppDbContext.TodoSchema);
        schemas.ShouldContain(AuthDbContext.IdentitySchema);
    }

    /// <summary>
    /// One history per context, and each in its own schema. Two contexts sharing one history would
    /// let either half's migrations claim the other's had been applied; a half with no history of
    /// its own would never be migrated at all.
    /// </summary>
    [Fact]
    public async Task EachContextHasAMigrationsHistoryOfItsOwn()
    {
        var histories = await Database.QueryAsync(
            $"""
            SELECT table_schema
            FROM information_schema.tables
            WHERE table_name = '{AppDbContext.MigrationsHistoryTableName}'
            ORDER BY table_schema
            """,
            TestToken);

        histories.ShouldBe([AuthDbContext.MigrationsHistorySchema, AppDbContext.MigrationsHistorySchema]);
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

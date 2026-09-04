using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Health;

/// <summary>
/// Liveness answers without touching a dependency; readiness answers for the database.
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
    /// The readiness endpoint answers in minimal plaintext, so its body names no individual check. This
    /// is what proves the database is what readiness reports on: exactly one context check is
    /// registered, and it carries the tag the endpoint filters by.
    /// </summary>
    [Fact]
    public void Readiness_CoversTheDatabase()
    {
        ReadyCheckNames().ShouldBe(["database"]);
    }

    /// <summary>
    /// Liveness must have no dependency at all: a database blip should not make an orchestrator
    /// restart a process that is running perfectly well.
    /// </summary>
    [Fact]
    public void Liveness_RunsNoChecks()
    {
        // The only registered check is a database check, and the liveness endpoint's predicate
        // excludes all of them.
        Registrations().Select(registration => registration.Name).ShouldBe(["database"]);
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
        schemas.ShouldContain(AppDbContext.IdentitySchema);
    }

    /// <summary>
    /// One context means one migrations history. Two histories could disagree about what had been
    /// applied, and a deployment could leave one feature's schema ahead of the other's.
    /// </summary>
    [Fact]
    public async Task ThereIsExactlyOneMigrationsHistoryTable()
    {
        var histories = await Database.QueryAsync(
            $"""
            SELECT table_schema
            FROM information_schema.tables
            WHERE table_name = '{AppDbContext.MigrationsHistoryTableName}'
            """,
            TestToken);

        histories.ShouldBe([AppDbContext.MigrationsHistorySchema]);
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

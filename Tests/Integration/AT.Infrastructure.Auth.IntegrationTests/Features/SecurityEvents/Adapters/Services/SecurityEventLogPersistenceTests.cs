using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.SecurityEvents.Model.Policies;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Configurations;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Rows.Models;
using AppTemplate.Infrastructure.Auth.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.IntegrationTests.Features.SecurityEvents.Adapters.Services;

public sealed class SecurityEventLogPersistenceTests(GrantTableFixture fixture) : IClassFixture<GrantTableFixture>
{
    private static readonly DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Record_CommitsTheRowAlone_AndFlushesNothingOfTheCallers()
    {
        var userId = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ISecurityEventLog>();

        context.Users.Add(new AppUser
        {
            Id = userId,
            UserName = $"pending-{userId:N}",
            SecurityStamp = Guid.CreateVersion7().ToString("N"),
            ConcurrencyStamp = Guid.CreateVersion7().ToString("N"),
        });

        log.Record(SecurityEvent.LoginSucceeded(userId));

        (await context.SecurityEvents.CountAsync(securityEvent => securityEvent.UserId == userId, TestToken))
            .ShouldBe(1, "the row commits immediately, on its own connection");
        context.ChangeTracker.Entries<AppUser>()
            .ShouldContain(entry => entry.State == EntityState.Added, "the caller's pending work is not flushed with it");
    }

    [Fact]
    public async Task Record_CutsTheOriginAndDetail_ToTheColumnsWidths()
    {
        var userId = Guid.CreateVersion7();

        await using var scope = fixture.Services.CreateAsyncScope();
        var contextOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<AuthDbContext>>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SecurityEventLogOptions>>();

        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(_now);

        var log = new SecurityEventLog(
            NullLogger<SecurityEventLog>.Instance,
            contextOptions,
            scope.ServiceProvider.GetRequiredService<AuthDbContext>(),
            new FixedOriginProvider(new string('i', 100), new string('u', 1000)),
            clock,
            options);

        log.Record(SecurityEvent.RoleGranted(userId, new string('r', 100)));

        var stored = await QueryRowAsync(fixture, userId);

        stored.ShouldNotBeNull();
        stored.IpAddress!.Length.ShouldBe(SecurityEventConfiguration.IpAddressLength);
        stored.UserAgent!.Length.ShouldBe(SecurityEventConfiguration.UserAgentLength);
        stored.Detail!.Length.ShouldBe(SecurityEventConfiguration.DetailLength);
        stored.OccurredAt.ShouldBe(_now);
    }

    [Fact]
    public async Task PurgeExpiredAsync_RemovesOnlyWhatHasAgedOut_AndAnswersTheCount()
    {
        var account = Guid.CreateVersion7();
        await SeedAsync(
            fixture,
            AnEvent(account, _now.AddDays(-100)),
            AnEvent(account, _now.AddDays(-10)));

        await using var scope = fixture.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ISecurityEventLog>();

        int purged = await log.PurgeExpiredAsync(_now, TestToken);

        purged.ShouldBe(1, "the default retention is 90 days: the 100-day-old row goes, the 10-day-old one stays");
        (await CountRowsAsync(fixture, account)).ShouldBe(1);
    }

    [Fact]
    public async Task GetForUserAsync_AnswersOneAccountsTrail_NewestFirst()
    {
        var account = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();
        await SeedAsync(
            fixture,
            AnEvent(account, _now.AddDays(-3)),
            AnEvent(account, _now.AddDays(-1)),
            AnEvent(account, _now),
            AnEvent(other, _now));

        await using var scope = fixture.Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<ISecurityEventQueries>();
        var paging = PageRequest.Create(PagingMode.Offset, 1, 2, cursor: null, SecurityEventCollectionPolicy.Instance);

        var page = await queries.GetForUserAsync(account, paging.Value, TestToken);

        page.TotalCount.ShouldBe(3, "the other account's row is not this account's trail");
        page.Items.Count.ShouldBe(2);
        page.Items[0].OccurredAt.ShouldBe(_now);
        page.Items[1].OccurredAt.ShouldBe(_now.AddDays(-1));
        page.HasNextPage.ShouldBeTrue();
    }

    [Fact]
    public async Task Record_KeepsAnEventWithNoAccount()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var log = scope.ServiceProvider.GetRequiredService<ISecurityEventLog>();

        log.Record(SecurityEvent.AuthenticationFailed(null, CredentialCheckStatus.NoSuchAccount));

        (await CountRowsAsync(fixture, null)).ShouldBe(1, "the user id is nullable by design: the address is the one thing the trail will not carry");
    }

    private static SecurityEventRecord AnEvent(Guid userId, DateTimeOffset occurredAt) =>
        new() { UserId = userId, Kind = SecurityEventKind.LoginSucceeded, OccurredAt = occurredAt };

    private static async Task SeedAsync(GrantTableFixture fixture, params SecurityEventRecord[] events)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        context.SecurityEvents.AddRange(events);
        await context.SaveChangesAsync(TestToken);
    }

    private static async Task<SecurityEventRecord?> QueryRowAsync(GrantTableFixture fixture, Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        return await context.SecurityEvents.AsNoTracking()
            .SingleOrDefaultAsync(securityEvent => securityEvent.UserId == userId, TestToken);
    }

    private static async Task<int> CountRowsAsync(GrantTableFixture fixture, Guid? userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        return await context.SecurityEvents.AsNoTracking()
            .CountAsync(securityEvent => securityEvent.UserId == userId, TestToken);
    }

    private sealed class FixedOriginProvider(string ipAddress, string userAgent) : ISecurityEventOriginProvider
    {
        public string? IpAddress => ipAddress;

        public string? UserAgent => userAgent;
    }
}

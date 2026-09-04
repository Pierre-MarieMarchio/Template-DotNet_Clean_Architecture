using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Online password guessing is bounded by lockout. Without <c>lockoutOnFailure: true</c> on the
/// sign-in call, <c>AccessFailedCount</c> never moves and guessing is bounded only by request rate.
/// </summary>
public sealed class AccountLockoutTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AfterTheConfiguredNumberOfFailures_EvenTheCorrectPasswordIsRefused()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        for (int attempt = 1; attempt <= ApiFactory.LockoutMaxFailedAccessAttempts; attempt++)
        {
            using var failed = await AttemptAsync(client, user.Email, "Wrong!Password9");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var withTheRightPassword = await AttemptAsync(client, user.Email, user.Password);

        withTheRightPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Lockout is not disclosed: saying so would confirm the account exists.
        (await ApiJson.ReadProblemAsync(withTheRightPassword, TestToken)).Code
            .ShouldBe("auth.login.invalidCredentials");
    }

    /// <summary>
    /// The counter climbs on the way to the threshold and the lockout window is written when it is
    /// reached. Note that ASP.NET Identity <em>resets</em> the counter at that moment — once the
    /// lockout is in force the counter has done its job — so the durable evidence of a lockout is
    /// <c>LockoutEnd</c>, not the count.
    /// </summary>
    [Fact]
    public async Task TheFailureCounterClimbs_AndTheLockoutWindowIsWrittenWhenItTrips()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        var before = await ReadUserAsync(user.Email);
        before.AccessFailedCount.ShouldBe(0);
        before.LockoutEnd.ShouldBeNull();

        for (int attempt = 1; attempt < ApiFactory.LockoutMaxFailedAccessAttempts; attempt++)
        {
            using var failed = await AttemptAsync(client, user.Email, "Wrong!Password9");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var approaching = await ReadUserAsync(user.Email);
        approaching.AccessFailedCount.ShouldBe(ApiFactory.LockoutMaxFailedAccessAttempts - 1);
        approaching.LockoutEnd.ShouldBeNull();

        using (var trips = await AttemptAsync(client, user.Email, "Wrong!Password9"))
        {
            trips.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var locked = await ReadUserAsync(user.Email);
        locked.LockoutEnd.ShouldNotBeNull();
        locked.LockoutEnd!.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        locked.AccessFailedCount.ShouldBe(0);
    }

    /// <summary>
    /// One failure short of the limit must still let the real user in, or "lockout works" would be
    /// satisfied by an endpoint that simply never accepts anything.
    /// </summary>
    [Fact]
    public async Task OneFailureShortOfTheLimit_TheCorrectPasswordStillWorks()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        for (int attempt = 1; attempt < ApiFactory.LockoutMaxFailedAccessAttempts; attempt++)
        {
            using var failed = await AttemptAsync(client, user.Email, "Wrong!Password9");
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var succeeded = await AttemptAsync(client, user.Email, user.Password);

        succeeded.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A successful sign-in resets the counter, so the next attacker starts from zero rather than
        // from wherever the real user happened to leave it.
        (await ReadUserAsync(user.Email)).AccessFailedCount.ShouldBe(0);
    }

    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync($"{AuthRoute}/login", new LoginRequest(email, password), TestToken);

    private async Task<AppUser> ReadUserAsync(string email)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, TestToken);
    }
}

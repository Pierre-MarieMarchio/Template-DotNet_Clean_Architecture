using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

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

        (await ApiJson.ReadProblemAsync(withTheRightPassword, TestToken)).Code
            .ShouldBe("auth.login.invalidCredentials");
    }

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

        (await ReadUserAsync(user.Email)).AccessFailedCount.ShouldBe(0);
    }

    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync($"{AuthRoute}/login", new LoginRequest(email, password), TestToken);

    private async Task<AppUser> ReadUserAsync(string email)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        return await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Email == email, TestToken);
    }
}

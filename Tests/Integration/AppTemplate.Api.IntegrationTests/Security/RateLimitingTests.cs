using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class RateLimitingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly int _permitsPerMinute = new RateLimitingOptions().AuthenticationPermitLimit;

    [Fact]
    public async Task OnceThePerCallerLimitIsExceeded_TheNextRequestIs429()
    {
        var client = CreateClient();

        for (int request = 1; request <= _permitsPerMinute; request++)
        {
            using var allowed = await AttemptLoginAsync(client);

            allowed.StatusCode.ShouldBe(
                HttpStatusCode.Unauthorized,
                $"request {request} of {_permitsPerMinute} should have been let through to the endpoint");
        }

        using var rejected = await AttemptLoginAsync(client);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var problem = await ApiJson.ReadProblemAsync(rejected, TestToken);
        problem.Code.ShouldBe("rateLimit.exceeded");
        problem.Status.ShouldBe(429);

        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task TheBudget_IsSharedAcrossTheAuthenticationEndpoints()
    {
        var client = CreateClient();

        for (int request = 1; request <= _permitsPerMinute; request++)
        {
            using var allowed = await AttemptLoginAsync(client);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var registration = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest("late-comer@integration.test", ValidPassword),
            TestToken);

        registration.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ExhaustingOneCallersBudget_LeavesAnotherCallerUnaffected()
    {
        var exhausted = CreateClient();
        var other = CreateClient();

        for (int request = 1; request <= _permitsPerMinute; request++)
        {
            using var allowed = await AttemptLoginAsync(exhausted);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var rejected = await AttemptLoginAsync(exhausted);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        using var fromTheOtherCaller = await AttemptLoginAsync(other);
        fromTheOtherCaller.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAuthenticationPolicy_SharesItsBudget_BetweenTwoSignedInCallersOnTheSameAddress()
    {
        const string sharedAddress = "10.20.30.40";
        int permitsSpent = 0;

        var first = CreateClientWithAddress(sharedAddress);
        var second = CreateClientWithAddress(sharedAddress);

        var firstUser = await RegisterConfirmedUserAsync(first, "shared-addr-first");
        permitsSpent += 2;

        await LoginAsync(first, firstUser);
        permitsSpent += 1;

        await RegisterConfirmedUserAsync(second, "shared-addr-second");
        permitsSpent += 2;

        for (; permitsSpent < _permitsPerMinute; permitsSpent++)
        {
            using var allowed = await AttemptLoginAsync(first);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var rejected = await AttemptLoginAsync(second);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static Task<HttpResponseMessage> AttemptLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest("nobody-at-all@integration.test", ValidPassword),
            TestToken);
}

using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The authentication endpoints are throttled per caller: with lockout alone, guessing is bounded
/// per account but not per attacker.
/// </summary>
/// <remarks>
/// <para>
/// The limit is ten requests per minute per client address. These tests are the only ones in the
/// suite that deliberately reuse one client for many requests — everywhere else each client gets its
/// own address so that a test cannot spend another's budget.
/// </para>
/// </remarks>
public sealed class RateLimitingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Matches <c>RateLimitingPolicies</c>' fixed-window permit limit.</summary>
    private const int _permitsPerMinute = 10;

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

    /// <summary>
    /// The budget belongs to the controller, not to one action, so spending it on login also closes
    /// registration. Otherwise an attacker just alternates endpoints.
    /// </summary>
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
            new RegisterCommand("late-comer", "late-comer@integration.test", ValidPassword),
            TestToken);

        registration.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Partitioned per address, so one attacker cannot exhaust everybody's budget. This is also what
    /// makes the rest of the suite viable.
    /// </summary>
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

    private static Task<HttpResponseMessage> AttemptLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginCommand("nobody-at-all@integration.test", ValidPassword),
            TestToken);
}

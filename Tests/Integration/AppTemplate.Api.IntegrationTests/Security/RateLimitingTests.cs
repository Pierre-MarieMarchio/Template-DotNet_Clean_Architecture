using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The authentication endpoints are throttled per caller: with lockout alone, guessing is bounded
/// per account but not per attacker.
/// </summary>
/// <remarks>
/// <para>
/// The limit is ten requests per window per client address. These tests are among the few in the
/// suite that deliberately reuse one client for many requests, or pin two clients to the same
/// address — everywhere else each client gets its own address so that a test cannot spend another's
/// budget. The window itself is widened for this host (see <c>ApiFactory</c>) so a slow run cannot
/// cross a real boundary mid-test; the permit counts asserted here are the real production ones.
/// </para>
/// </remarks>
public sealed class RateLimitingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const int _permitsPerMinute = RateLimitingPolicies.AuthenticationPermitLimit;

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
            new RegisterRequest("late-comer", "late-comer@integration.test", ValidPassword),
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

    /// <summary>
    /// Mirrors <see cref="ExhaustingOneCallersBudget_LeavesAnotherCallerUnaffected"/> in reverse: two
    /// distinct signed-in callers pinned to the same address on purpose. The authentication policy
    /// must not tell them apart — it exists to slow down credential guessing on endpoints where,
    /// by construction, there is no identity yet, and reading one here would defeat that.
    /// </summary>
    [Fact]
    public async Task TheAuthenticationPolicy_SharesItsBudget_BetweenTwoSignedInCallersOnTheSameAddress()
    {
        const string sharedAddress = "10.20.30.40";
        int permitsSpent = 0;

        var first = CreateClientWithAddress(sharedAddress);
        var second = CreateClientWithAddress(sharedAddress);

        var firstUser = await RegisterConfirmedUserAsync(first, "shared-addr-first");
        permitsSpent += 2; // register, confirm-email

        await LoginAsync(first, firstUser);
        permitsSpent += 1;

        await RegisterConfirmedUserAsync(second, "shared-addr-second");
        permitsSpent += 2;

        for (; permitsSpent < _permitsPerMinute; permitsSpent++)
        {
            using var allowed = await AttemptLoginAsync(first);
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // The address's budget is now spent on `first`'s traffic alone. `second` shares the address,
        // so it finds the same empty budget despite never having failed a login itself.
        using var rejected = await AttemptLoginAsync(second);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static Task<HttpResponseMessage> AttemptLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest("nobody-at-all@integration.test", ValidPassword),
            TestToken);
}

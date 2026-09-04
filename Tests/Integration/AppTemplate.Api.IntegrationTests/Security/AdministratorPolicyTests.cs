using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// Policy-based authorisation on the maintenance endpoint: an ordinary authenticated user is not an
/// administrator, and an unauthenticated caller is not even that.
/// </summary>
/// <remarks>
/// This covers the 401/403 half only. Reaching the 200 path needs an account actually placed in the
/// seeded <c>Admin</c> role, and the test host seeds no identity data at all
/// (<c>IdentitySeed:Enabled</c> is <c>false</c> for the whole suite — see <c>ApiFactory</c>) and
/// exposes no route that could grant a role to an arbitrary registered user without reaching for
/// <c>AppUser</c>/<c>AppRole</c>, which are internal to the persistence module and, by this project's
/// own rule, not something an integration test may name. The 200 path is therefore not exercised
/// end to end here; it would need either a seeded administrator wired into the test host or a
/// dedicated administrative endpoint to grant the role, neither of which exists yet.
/// </remarks>
public sealed class AdministratorPolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _route = "/api/v1/maintenance/idempotency-keys/expired";

    [Fact]
    public async Task AnOrdinaryAuthenticatedUser_Gets403WithACode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.DeleteAsync(new Uri(_route, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldNotBeNullOrWhiteSpace(problem.Body);
    }

    [Fact]
    public async Task AnUnauthenticatedCaller_Gets401()
    {
        var client = CreateClient();

        using var response = await client.DeleteAsync(new Uri(_route, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

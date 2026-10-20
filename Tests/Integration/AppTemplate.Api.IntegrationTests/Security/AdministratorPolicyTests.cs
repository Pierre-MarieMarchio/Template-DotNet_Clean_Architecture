using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class AdministratorPolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("/api/v1/maintenance/idempotency-keys/expired")]
    [InlineData("/api/v1/maintenance/refresh-tokens/expired")]
    public async Task AnOrdinaryAuthenticatedUser_Gets403WithACode(string route)
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.DeleteAsync(new Uri(route, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldNotBeNullOrWhiteSpace(problem.Body);
    }

    [Theory]
    [InlineData("/api/v1/maintenance/idempotency-keys/expired")]
    [InlineData("/api/v1/maintenance/refresh-tokens/expired")]
    public async Task AnUnauthenticatedCaller_Gets401(string route)
    {
        var client = CreateClient();

        using var response = await client.DeleteAsync(new Uri(route, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

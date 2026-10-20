using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class SecurityStampRotationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ChangingPassword_InvalidatesTheAccessTokenAlreadyInCirculation()
    {
        var (client, user, _) = await SignInAsync();

        using var before = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var changed = await client.PostAsJsonAsync(
            $"{AuthRoute}/change-password",
            new ChangePasswordRequest(user.Password, "Rotated!Password2"),
            TestToken);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var after = await client.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        after.Headers.GetValues(ApiFactory.AuthFailureHeader)
            .ShouldContain("This token's security stamp is no longer valid.");
    }
}

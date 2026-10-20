using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class LoginResponseContractTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Login_PublishesTheStatusDiscriminantOnTheWire()
    {
        var client = CreateClient();
        var user = await RegisterConfirmedUserAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

        document.RootElement.GetProperty("status").GetString().ShouldBe("authenticated");
    }

    [Fact]
    public async Task Login_OnATwoFactorAccount_PublishesTheTwoFactorRequiredBranchOnTheWire()
    {
        var (client, user, session) = await SignInAsync();
        await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        var loginClient = CreateClient();

        using var response = await loginClient.PostAsJsonAsync(
            $"{AuthRoute}/login",
            new LoginRequest(user.Email, user.Password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

        document.RootElement.GetProperty("status").GetString().ShouldBe("twoFactorRequired");
        document.RootElement.GetProperty("challengeToken").GetString().ShouldNotBeNullOrEmpty();
    }
}

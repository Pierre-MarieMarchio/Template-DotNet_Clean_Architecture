using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// The <c>status</c> discriminant on the wire, read as raw JSON rather than deserialised into
/// <c>LoginResponse</c>. MVC decides what actually gets written from <c>ObjectResult.DeclaredType</c>,
/// which a typed round trip never exercises — only reading what the real endpoint sent proves the
/// discriminant is really there.
/// </summary>
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
}

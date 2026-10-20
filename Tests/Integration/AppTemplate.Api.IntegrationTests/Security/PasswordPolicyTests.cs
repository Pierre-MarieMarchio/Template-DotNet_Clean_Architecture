using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

public sealed class PasswordPolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    public static TheoryData<string, string> RejectedPasswords => new()
    {
        { "", "empty" },

        { "Sh0rt!A", "seven characters, under the configured twelve" },
        { "Sh0rt!Aa", "eight characters, under the configured twelve" },
        { "Ab1!Ab1!Ab1", "eleven characters, one under the configured twelve" },

        { "nodigitsorcaps!", "no digit and no uppercase" },
        { "NoSpecials12345", "no non-alphanumeric character" },
        { "NOLOWERCASE1234!", "no lowercase character" },
        { "nouppercase1234!", "no uppercase character" },
    };

    [Theory]
    [MemberData(nameof(RejectedPasswords))]
    public async Task AWeakPassword_IsRejected(string password, string why)
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest("candidate@integration.test", password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("request.validationFailed", why);
        problem.Status.ShouldBe(400);

        problem.Body.ShouldContain("password", Case.Sensitive, why);
    }

    [Theory]
    [InlineData("AAAAaaaa!!!!1111")]
    [InlineData("Twelve!Char1")]
    public async Task APasswordThatMeetsEveryRule_IsAccepted(string password)
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest("candidate@integration.test", password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task ARejectedRegistration_CreatesNoAccountAndDoesNotTakeTheAddress()
    {
        var client = CreateClient();
        const string email = "second-attempt@integration.test";

        using var rejected = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest(email, "weakweak"),
            TestToken);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        int users = await Database.CountAsync("""SELECT count(*) FROM identity."User" """, TestToken);
        users.ShouldBe(0);

        using var accepted = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest(email, ValidPassword),
            TestToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    public async Task AMalformedEmail_IsRejectedByTheShapeValidator(string email)
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterRequest(email, ValidPassword),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("request.validationFailed");
    }
}

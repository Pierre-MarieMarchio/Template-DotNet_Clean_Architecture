using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The password policy is enforced at registration.
/// </summary>
/// <remarks>
/// One authority: the configured <c>Identity</c> policy, which the test host sets to twelve
/// characters with all four character classes required. The shape validator checks presence only.
/// </remarks>
public sealed class PasswordPolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// Each case, with what it is there to exercise. The shape validator and the store report the
    /// one validation code the application has, both on the <c>password</c> field.
    /// </summary>
    public static TheoryData<string, string> RejectedPasswords => new()
    {
        // Refused by the shape validator: nothing to judge.
        { "", "empty" },

        // Rejected by the configured policy. These prove the *configured* length is live, and not
        // only the hard floor configuration cannot go below.
        { "Sh0rt!A", "seven characters, under the configured twelve" },
        { "Sh0rt!Aa", "eight characters, under the configured twelve" },
        { "Ab1!Ab1!Ab1", "eleven characters, one under the configured twelve" },

        // Long enough, but missing one required character class each.
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

        // The failure names the field it is about, so a client can render it next to the input it
        // concerns rather than as free text.
        problem.Body.ShouldContain("password", Case.Sensitive, why);
    }

    /// <summary>
    /// The control. Without it, "the policy is enforced" would be satisfied by an endpoint that
    /// rejects every password there is.
    /// </summary>
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

    /// <summary>
    /// A rejected registration must leave nothing behind, or the address is taken by an account
    /// nobody can sign in to.
    /// </summary>
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

        // The address is still free.
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

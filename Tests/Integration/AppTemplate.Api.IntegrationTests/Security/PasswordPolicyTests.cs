using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Auth.UseCases.Commands;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The password policy is enforced at registration.
/// </summary>
/// <remarks>
/// Two layers, both asserted: the shape validator's absolute floor of eight characters, and the
/// configured <c>Identity</c> policy, which the test host sets to twelve characters with all four
/// character classes required.
/// </remarks>
public sealed class PasswordPolicyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Each case is rejected, and by which layer.</summary>
    public static TheoryData<string, string, string> RejectedPasswords => new()
    {
        // Below the shape validator's floor, so it never reaches the user store.
        { "Sh0rt!A", "auth.validation", "under the absolute minimum length" },
        { "", "auth.validation", "empty" },

        // Acceptable to the shape validator, rejected by the configured policy. This pair is what
        // proves the *configured* length is live and not just the hard floor.
        { "Sh0rt!Aa", "auth.register.rejected", "eight characters, under the configured twelve" },
        { "Ab1!Ab1!Ab1", "auth.register.rejected", "eleven characters, one under the configured twelve" },

        // Long enough, but missing one required character class each.
        { "nodigitsorcaps!", "auth.register.rejected", "no digit and no uppercase" },
        { "NoSpecials12345", "auth.register.rejected", "no non-alphanumeric character" },
        { "NOLOWERCASE1234!", "auth.register.rejected", "no lowercase character" },
        { "nouppercase1234!", "auth.register.rejected", "no uppercase character" },
    };

    [Theory]
    [MemberData(nameof(RejectedPasswords))]
    public async Task AWeakPassword_IsRejected(string password, string expectedCode, string why)
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterCommand("candidate", "candidate@integration.test", password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, why);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe(expectedCode, why);
        problem.Status.ShouldBe(400);
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
            new RegisterCommand("candidate", "candidate@integration.test", password),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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
            new RegisterCommand("second-attempt", email, "weakweak"),
            TestToken);

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        int users = await Database.CountAsync("""SELECT count(*) FROM identity."User" """, TestToken);
        users.ShouldBe(0);

        // The address is still free.
        using var accepted = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterCommand("second-attempt", email, ValidPassword),
            TestToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ADuplicateEmail_IsAConflictWithANeutralMessage()
    {
        var client = CreateClient();
        var existing = await RegisterUserAsync(client, "first");

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterCommand("someone-else", existing.Email, ValidPassword),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("auth.register.unavailable");

        // Registration cannot fully hide that an address is taken, but the message must not confirm
        // which of the two values collided.
        problem.Detail.ShouldBe("That username or email address cannot be used.");
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    public async Task AMalformedEmail_IsRejectedByTheShapeValidator(string email)
    {
        var client = CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"{AuthRoute}/register",
            new RegisterCommand("candidate", email, ValidPassword),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.validation");
    }
}

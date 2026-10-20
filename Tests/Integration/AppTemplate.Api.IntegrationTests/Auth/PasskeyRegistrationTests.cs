using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class PasskeyRegistrationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _validationFailed = "request.validationFailed";

    private PasskeyCeremonyFailures Failures => PasskeyTestSupport.Failures(Fixture);

    [Fact]
    public async Task WithoutThePassword_IsRefused_AndTheSameCeremonyCompletesWithIt()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);
        string credential = authenticator.CreateCredential(options.CreationOptions, PasskeyTestSupport.Origin);

        using var refused = await PasskeyTestSupport.PostRegistrationAsync(
            client, credential, options.RegistrationState, currentPassword: null, TestToken);
        await ShouldBeRefusedOnAsync(refused, "currentPassword", "required");

        using var accepted = await PasskeyTestSupport.PostRegistrationAsync(
            client, credential, options.RegistrationState, user.Password, TestToken);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WithAWrongPassword_IsRefused()
    {
        var (client, _, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);

        using var refused = await PasskeyTestSupport.PostRegistrationAsync(
            client,
            authenticator.CreateCredential(options.CreationOptions, PasskeyTestSupport.Origin),
            options.RegistrationState,
            "Not!The1Password",
            TestToken);

        await ShouldBeRefusedOnAsync(refused, "currentPassword", "incorrect");
        (await PasskeyTestSupport.ListAsync(client, TestToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAccountWithNoPassword_RegistersOnARecentSignInAlone()
    {
        var client = CreateClient();
        var tokens = await PasskeyTestSupport.SignInExternallyAsync(Fixture, client, TestToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var authenticator = new SoftwareAuthenticator();

        await PasskeyTestSupport.RegisterAsync(client, authenticator, currentPassword: null, TestToken);

        var signedIn = await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
        signedIn.Tokens.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnAccountWithNoPassword_IsRefusedOnASignInOlderThanFiveMinutes()
    {
        var client = CreateClient();
        var tokens = await PasskeyTestSupport.SignInExternallyAsync(Fixture, client, TestToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var authenticator = new SoftwareAuthenticator();
        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);

        Clock.Advance(TimeSpan.FromMinutes(6));

        using var refused = await PasskeyTestSupport.PostRegistrationAsync(
            client,
            authenticator.CreateCredential(options.CreationOptions, PasskeyTestSupport.Origin),
            options.RegistrationState,
            currentPassword: null,
            TestToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiJson.ReadProblemAsync(refused, TestToken)).Code.ShouldBe("auth.account.reAuthenticationRequired");
    }

    [Fact]
    public async Task AClientDataOriginOtherThanTheRequestsOrigin_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            options => authenticator.CreateCredential(
                options, PasskeyTestSupport.Origin, clientDataOrigin: "https://attacker.example"),
            "invalid origin");

        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);
    }

    [Fact]
    public async Task ACeremonyWithNoOriginHeader_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            options => authenticator.CreateCredential(options, PasskeyTestSupport.Origin),
            "invalid origin",
            originHeader: null);
    }

    [Fact]
    public async Task ACredentialSignedOverAnotherChallenge_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            options => authenticator.CreateCredential(options, PasskeyTestSupport.Origin, challenge: "b3RoZXItY2hhbGxlbmdl"),
            "challenge does not match");
    }

    [Fact]
    public async Task ACredentialScopedToAnotherRelyingParty_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);
        ((string?)JsonNode.Parse(options.CreationOptions)!["rp"]!["id"]).ShouldBe(PasskeyTestSupport.RpId);

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            creationOptions => authenticator.CreateCredential(creationOptions, PasskeyTestSupport.Origin, rpId: "localhost"),
            "Relying Party ID hash");
    }

    [Fact]
    public async Task ACredentialCreatedWithoutUserVerification_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            options => authenticator.CreateCredential(options, PasskeyTestSupport.Origin, flags: 0x41),
            "'UserVerified' flag");
    }

    [Fact]
    public async Task AnAssertionPresentedAsARegistration_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            options => authenticator.CreateCredential(options, PasskeyTestSupport.Origin, clientDataType: "webauthn.get"),
            "'type' field");
    }

    [Fact]
    public async Task ACredentialAlreadyRegistered_IsRefused()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);
        string credential = authenticator.CreateCredential(options.CreationOptions, PasskeyTestSupport.Origin);

        using var registered = await PasskeyTestSupport.PostRegistrationAsync(
            client, credential, options.RegistrationState, user.Password, TestToken);
        registered.StatusCode.ShouldBe(HttpStatusCode.OK);

        Failures.Clear();
        using var replayed = await PasskeyTestSupport.PostRegistrationAsync(
            client, credential, options.RegistrationState, user.Password, TestToken);
        await ShouldBeRefusedOnAsync(replayed, "credential");
        Failures.Last.ShouldBeNull();

        await ShouldBeRefusedByIdentityAsync(
            client,
            user.Password,
            creationOptions => authenticator.CreateCredential(creationOptions, PasskeyTestSupport.Origin),
            "already registered");

        (await PasskeyTestSupport.ListAsync(client, TestToken)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task AStateIssuedToAnotherAccount_IsRefused()
    {
        var (victim, _, _) = await SignInAsync("victim");
        var (attacker, attackerUser, _) = await SignInAsync("attacker");
        using var authenticator = new SoftwareAuthenticator();
        var victimOptions = await PasskeyTestSupport.BeginRegistrationAsync(victim, TestToken);

        Failures.Clear();
        using var refused = await PasskeyTestSupport.PostRegistrationAsync(
            attacker,
            authenticator.CreateCredential(victimOptions.CreationOptions, PasskeyTestSupport.Origin),
            victimOptions.RegistrationState,
            attackerUser.Password,
            TestToken);

        await ShouldBeRefusedOnAsync(refused, "credential");
        Failures.Last.ShouldBeNull();
        (await PasskeyTestSupport.ListAsync(victim, TestToken)).ShouldBeEmpty();
        (await PasskeyTestSupport.ListAsync(attacker, TestToken)).ShouldBeEmpty();
    }

    private async Task ShouldBeRefusedByIdentityAsync(
        HttpClient client,
        string password,
        Func<string, string> createCredential,
        string reason,
        string? originHeader = PasskeyTestSupport.Origin)
    {
        var options = await PasskeyTestSupport.BeginRegistrationAsync(client, TestToken);

        Failures.Clear();
        using var refused = await PasskeyTestSupport.PostRegistrationAsync(
            client,
            createCredential(options.CreationOptions),
            options.RegistrationState,
            password,
            TestToken,
            originHeader: originHeader);

        await ShouldBeRefusedOnAsync(refused, "credential");
        Failures.Last.ShouldNotBeNull().ShouldContain(reason, Case.Insensitive);
    }

    private static async Task ShouldBeRefusedOnAsync(HttpResponseMessage response, string field, string? message = null)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe(_validationFailed);
        problem.Body.ShouldContain($"\"{field}\"", Case.Sensitive);

        if (message is not null)
        {
            problem.Body.ShouldContain(message, Case.Insensitive);
        }
    }
}

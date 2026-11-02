using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class PasskeyFlowTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ARegisteredPasskey_SignsTheAccountIn()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        var device = CreateClient();
        var signedIn = await PasskeyTestSupport.SignInAsync(device, authenticator, TestToken);

        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens.AccessToken);
        (await ReadOwnUserIdAsync(device)).ShouldBe(session.UserId);

        using var refreshed = await device.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(signedIn.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await ApiJson.ReadAsync<TokenResponse>(refreshed, TestToken);

        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var sessions = await device.GetAsync(new Uri($"{AuthRoute}/sessions", UriKind.Relative), TestToken);
        sessions.StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = (await ApiJson.ReadAsync<SessionsResponse>(sessions, TestToken)).Sessions;
        listed.Count.ShouldBe(2);
        listed.Count(listedSession => listedSession.Current).ShouldBe(1);
    }

    [Fact]
    public async Task TheSignInOptions_TakeNoAddress_AndNameNoCredential()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        var options = await PasskeyTestSupport.BeginSignInAsync(CreateClient(), TestToken);
        var publicKey = JsonNode.Parse(options.SignInOptions)!.AsObject();

        (publicKey["allowCredentials"]?.AsArray().Count ?? 0).ShouldBe(0);
        options.SignInOptions.ShouldNotContain(user.Email, Case.Insensitive);
    }

    [Fact]
    public async Task ARegisteredPasskey_IsListedWithItsIdNameAndCreationDate()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();

        var registered = await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken, name: "Laptop");

        registered.Id.ShouldBe(authenticator.CredentialIdBase64Url);
        registered.Name.ShouldBe("Laptop");

        var listed = (await PasskeyTestSupport.ListAsync(client, TestToken)).ShouldHaveSingleItem();
        listed.Id.ShouldBe(authenticator.CredentialIdBase64Url);
        listed.Name.ShouldBe("Laptop");
        listed.CreatedAt.ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Passkeys_AreListedNewestFirst()
    {
        var (client, user, _) = await SignInAsync();
        using var first = new SoftwareAuthenticator();
        using var second = new SoftwareAuthenticator();

        await PasskeyTestSupport.RegisterAsync(client, first, user.Password, TestToken, name: "First");
        await PasskeyTestSupport.RegisterAsync(client, second, user.Password, TestToken, name: "Second");

        (await PasskeyTestSupport.ListAsync(client, TestToken)).Select(passkey => passkey.Id)
            .ShouldBe([second.CredentialIdBase64Url, first.CredentialIdBase64Url]);
    }

    [Fact]
    public async Task APasskeySignIn_IsNotChallengedForTheSecondFactor()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);
        await TwoFactorTestSupport.EnableTwoFactorAsync(client, user, session, TestToken);

        await TwoFactorTestSupport.LoginExpectingChallengeAsync(CreateClient(), user, TestToken);

        var signedIn = await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
        signedIn.Tokens.AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task EachSignIn_PersistsTheAuthenticatorsCounter()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);
        (await PasskeyTestSupport.SignCountAsync(Database, session.UserId, TestToken)).ShouldBe(0u);

        await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
        (await PasskeyTestSupport.SignCountAsync(Database, session.UserId, TestToken)).ShouldBe(1u);

        await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
        (await PasskeyTestSupport.SignCountAsync(Database, session.UserId, TestToken)).ShouldBe(2u);
    }

    [Fact]
    public async Task AnAuthenticatorThatKeepsNoCounter_SignsInAgainAndAgain()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator { KeepsNoCounter = true, BackupEligible = true, BackedUp = true };
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
        }

        (await PasskeyTestSupport.SignCountAsync(Database, session.UserId, TestToken)).ShouldBe(0u);
    }

    [Fact]
    public async Task RegisteringAPasskey_EndsNoSession()
    {
        var (client, user, session) = await SignInAsync();
        var otherDevice = CreateClient();
        var otherTokens = await LoginAsync(otherDevice, user);
        using var authenticator = new SoftwareAuthenticator();

        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        (await ReadOwnUserIdAsync(client)).ShouldBe(session.UserId);

        using var otherRefreshed = await otherDevice.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(otherTokens.RefreshToken),
            TestToken);
        otherRefreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

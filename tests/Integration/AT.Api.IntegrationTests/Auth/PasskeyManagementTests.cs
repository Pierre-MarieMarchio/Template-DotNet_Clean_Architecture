using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class PasskeyManagementTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _notFound = "auth.passkey.notFound";

    [Fact]
    public async Task ARemovedPasskey_IsNoLongerListed_AndNoLongerSignsIn()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        using var removed = await RemoveAsync(client, authenticator.CredentialIdBase64Url);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await TwoFactorTestSupport.RefreshAuthorizationAsync(client, session.Tokens.RefreshToken, TestToken);
        (await PasskeyTestSupport.ListAsync(client, TestToken)).ShouldBeEmpty();

        var device = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(device, TestToken);
        using var refused = await PasskeyTestSupport.PostSignInAsync(
            device,
            authenticator.GetAssertion(options.SignInOptions, PasskeyTestSupport.Origin),
            options.AuthenticationState,
            TestToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refused, TestToken)).Code.ShouldBe("auth.passkeySignIn.invalid");
    }

    [Theory]
    [InlineData("other")]
    [InlineData("unknown")]
    [InlineData("malformed")]
    public async Task AnIdTheAccountDoesNotHold_AnswersNotFound_AndRemovesNothing(string kind)
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        var (otherClient, otherUser, _) = await SignInAsync("other");
        using var otherAuthenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(otherClient, otherAuthenticator, otherUser.Password, TestToken);

        string id = kind switch
        {
            "other" => otherAuthenticator.CredentialIdBase64Url,
            "unknown" => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            _ => "not*base64url",
        };

        using var response = await RemoveAsync(client, id);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe(_notFound);

        (await PasskeyTestSupport.ListAsync(client, TestToken)).ShouldHaveSingleItem();
        (await PasskeyTestSupport.ListAsync(otherClient, TestToken)).ShouldHaveSingleItem();
        await PasskeyTestSupport.SignInAsync(CreateClient(), otherAuthenticator, TestToken);
    }

    [Fact]
    public async Task RemovingAPasskeyTwice_AnswersNotFoundTheSecondTime()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        using (var first = await RemoveAsync(client, authenticator.CredentialIdBase64Url))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await TwoFactorTestSupport.RefreshAuthorizationAsync(client, session.Tokens.RefreshToken, TestToken);

        using var second = await RemoveAsync(client, authenticator.CredentialIdBase64Url);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(second, TestToken)).Code.ShouldBe(_notFound);
    }

    [Fact]
    public async Task RemovingAPasskey_EndsTheSessionItSignedIn()
    {
        var (client, user, _) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        var device = CreateClient();
        var signedIn = await PasskeyTestSupport.SignInAsync(device, authenticator, TestToken);

        using var removed = await RemoveAsync(client, authenticator.CredentialIdBase64Url);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        device.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signedIn.Tokens.AccessToken);
        using var me = await device.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var refreshed = await device.PostAsJsonAsync(
            $"{AuthRoute}/refresh",
            new RefreshAccessTokenRequest(signedIn.Tokens.RefreshToken),
            TestToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeletingTheAccount_DeletesItsPasskeys()
    {
        var (client, user, session) = await SignInAsync();
        using var authenticator = new SoftwareAuthenticator();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        using (var deleted = await client.PostAsJsonAsync(
            $"{AuthRoute}/delete-account",
            new DeleteMyAccountRequest(user.Password, TwoFactorCode: null),
            TestToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await Database.CountAsync(
            $"""SELECT COUNT(*) FROM {DatabaseServer.Table("identity", "UserPasskeys")} WHERE "UserId" = {DatabaseServer.Literal(session.UserId)}""",
            TestToken)).ShouldBe(0);

        var device = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(device, TestToken);
        using var refused = await PasskeyTestSupport.PostSignInAsync(
            device,
            authenticator.GetAssertion(options.SignInOptions, PasskeyTestSupport.Origin),
            options.AuthenticationState,
            TestToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, string credentialId) =>
        client.DeleteAsync(new Uri($"{AuthRoute}/passkeys/{credentialId}", UriKind.Relative), TestToken);
}

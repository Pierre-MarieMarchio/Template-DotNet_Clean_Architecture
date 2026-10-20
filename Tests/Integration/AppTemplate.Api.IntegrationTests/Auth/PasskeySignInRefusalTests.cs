using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// Passkey sign-ins Identity or the ceremony state refuses. Every refusal answers the same 401, so
/// each also asserts the reason recorded — or that none was, when the state refused it first.
/// </summary>
public sealed class PasskeySignInRefusalTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _signInInvalid = "auth.passkeySignIn.invalid";

    private const byte _userPresent = 0x01;
    private const byte _userVerified = 0x04;
    private const byte _backupEligible = 0x08;

    private PasskeyCeremonyFailures Failures => PasskeyTestSupport.Failures(Fixture);

    public enum Tamper
    {
        RelyingParty,
        NoUserVerification,
        Signature,
        BackupEligibility,
    }

    [Fact]
    public async Task AClientDataOriginOtherThanTheRequestsOrigin_IsRefused()
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);

        await ShouldBeRefusedByIdentityAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, clientDataOrigin: "https://attacker.example"),
            "invalid origin");

        await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);
    }

    [Fact]
    public async Task ACeremonyWithNoOriginHeader_IsRefused()
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);

        await ShouldBeRefusedByIdentityAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin),
            "invalid origin",
            originHeader: null);
    }

    [Fact]
    public async Task AnAssertionOverAnotherChallenge_IsRefused()
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);

        await ShouldBeRefusedByIdentityAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, challenge: "b3RoZXItY2hhbGxlbmdl"),
            "challenge does not match");
    }

    /// <summary>The captured pair is refused by its spent state, before Identity sees it, and the counter stays.</summary>
    [Fact]
    public async Task ReplayingASignIn_IsRefused_AndLeavesTheCounter()
    {
        using var authenticator = new SoftwareAuthenticator();
        var userId = await RegisterAsync(authenticator);
        var client = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(client, TestToken);
        string assertion = authenticator.GetAssertion(options.SignInOptions, PasskeyTestSupport.Origin);

        using var first = await PasskeyTestSupport.PostSignInAsync(client, assertion, options.AuthenticationState, TestToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        Failures.Clear();
        using var replayed = await PasskeyTestSupport.PostSignInAsync(client, assertion, options.AuthenticationState, TestToken);
        await ShouldBeRefusedAsync(replayed);
        Failures.Last.ShouldBeNull();

        (await PasskeyTestSupport.SignCountAsync(Database, userId, TestToken)).ShouldBe(1u);
    }

    /// <summary>
    /// A synced passkey reports zero every time, so Identity's counter check would accept the
    /// captured pair again: the spent state is what refuses it.
    /// </summary>
    [Fact]
    public async Task ReplayingASignIn_FromAnAuthenticatorWithNoCounter_IsRefused()
    {
        using var authenticator = new SoftwareAuthenticator { KeepsNoCounter = true, BackupEligible = true, BackedUp = true };
        await RegisterAsync(authenticator);
        var client = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(client, TestToken);
        string assertion = authenticator.GetAssertion(options.SignInOptions, PasskeyTestSupport.Origin);

        using var first = await PasskeyTestSupport.PostSignInAsync(client, assertion, options.AuthenticationState, TestToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        Failures.Clear();
        using var replayed = await PasskeyTestSupport.PostSignInAsync(client, assertion, options.AuthenticationState, TestToken);
        await ShouldBeRefusedAsync(replayed);
        Failures.Last.ShouldBeNull();
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(0u)]
    public async Task ACounterThatDidNotAdvance_IsRefused(uint presented)
    {
        using var authenticator = new SoftwareAuthenticator();
        var userId = await RegisterAsync(authenticator);
        await PasskeyTestSupport.SignInAsync(CreateClient(), authenticator, TestToken);

        await ShouldBeRefusedByIdentityAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, signCount: presented),
            "signature counter");

        (await PasskeyTestSupport.SignCountAsync(Database, userId, TestToken)).ShouldBe(1u);
    }

    [Theory]
    [InlineData(Tamper.RelyingParty, "Relying Party ID hash")]
    [InlineData(Tamper.NoUserVerification, "'UserVerified' flag")]
    [InlineData(Tamper.Signature, "signature was invalid")]
    [InlineData(Tamper.BackupEligibility, "unexpectedly eligible for backup")]
    public async Task ATamperedAssertion_IsRefused(Tamper tamper, string reason)
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);

        await ShouldBeRefusedByIdentityAsync(
            options => tamper switch
            {
                Tamper.RelyingParty => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, rpId: "localhost"),
                Tamper.NoUserVerification => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, flags: _userPresent),
                Tamper.Signature => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, corruptSignature: true),
                Tamper.BackupEligibility => authenticator.GetAssertion(
                    options, PasskeyTestSupport.Origin, flags: _userPresent | _userVerified | _backupEligible),
                _ => throw new ArgumentOutOfRangeException(nameof(tamper), tamper, null),
            },
            reason);
    }

    [Fact]
    public async Task AUserHandleNamingAnotherAccount_IsRefused()
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);
        var (_, _, other) = await SignInAsync("other");
        string otherHandle = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(other.UserId.ToString()));

        await ShouldBeRefusedByIdentityAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, userHandle: otherHandle),
            "does not belong to the specified user");
    }

    /// <summary>
    /// Nothing in the answer tells a refused signature from a spent state or an unknown credential.
    /// </summary>
    [Fact]
    public async Task EveryRefusal_AnswersTheSameBody()
    {
        using var authenticator = new SoftwareAuthenticator();
        await RegisterAsync(authenticator);
        using var stranger = new SoftwareAuthenticator();
        stranger.CreateCredential(
            new JsonObject
            {
                ["challenge"] = "c3RyYW5nZXI",
                ["rp"] = new JsonObject { ["id"] = PasskeyTestSupport.RpId },
                ["user"] = new JsonObject { ["id"] = "c3RyYW5nZXI" },
            }.ToJsonString(),
            PasskeyTestSupport.Origin);

        var badSignature = await RefusedSignInAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, corruptSignature: true));
        var unknownCredential = await RefusedSignInAsync(
            options => stranger.GetAssertion(options, PasskeyTestSupport.Origin));
        var otherChallenge = await RefusedSignInAsync(
            options => authenticator.GetAssertion(options, PasskeyTestSupport.Origin, challenge: "b3RoZXItY2hhbGxlbmdl"));

        var client = CreateClient();
        var spentOptions = await PasskeyTestSupport.BeginSignInAsync(client, TestToken);
        string assertion = authenticator.GetAssertion(spentOptions.SignInOptions, PasskeyTestSupport.Origin);
        using (var accepted = await PasskeyTestSupport.PostSignInAsync(client, assertion, spentOptions.AuthenticationState, TestToken))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var spent = await PasskeyTestSupport.PostSignInAsync(client, assertion, spentOptions.AuthenticationState, TestToken);
        spent.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var spentState = await ApiJson.ReadProblemAsync(spent, TestToken);

        badSignature.Code.ShouldBe(_signInInvalid);
        unknownCredential.BodyWithoutTraceId.ShouldBe(badSignature.BodyWithoutTraceId);
        otherChallenge.BodyWithoutTraceId.ShouldBe(badSignature.BodyWithoutTraceId);
        spentState.BodyWithoutTraceId.ShouldBe(badSignature.BodyWithoutTraceId);
    }

    /// <summary>Registers the authenticator's passkey to a fresh password account, whose id it returns.</summary>
    private async Task<Guid> RegisterAsync(SoftwareAuthenticator authenticator)
    {
        var (client, user, session) = await SignInAsync();
        await PasskeyTestSupport.RegisterAsync(client, authenticator, user.Password, TestToken);

        return session.UserId;
    }

    private async Task ShouldBeRefusedByIdentityAsync(
        Func<string, string> getAssertion,
        string reason,
        string? originHeader = PasskeyTestSupport.Origin)
    {
        var client = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(client, TestToken);

        Failures.Clear();
        using var refused = await PasskeyTestSupport.PostSignInAsync(
            client,
            getAssertion(options.SignInOptions),
            options.AuthenticationState,
            TestToken,
            originHeader);

        await ShouldBeRefusedAsync(refused);
        Failures.Last.ShouldNotBeNull().ShouldContain(reason, Case.Insensitive);
    }

    private async Task<ProblemResponse> RefusedSignInAsync(Func<string, string> getAssertion)
    {
        var client = CreateClient();
        var options = await PasskeyTestSupport.BeginSignInAsync(client, TestToken);

        using var refused = await PasskeyTestSupport.PostSignInAsync(
            client,
            getAssertion(options.SignInOptions),
            options.AuthenticationState,
            TestToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        return await ApiJson.ReadProblemAsync(refused, TestToken);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe(_signInInvalid);
    }
}

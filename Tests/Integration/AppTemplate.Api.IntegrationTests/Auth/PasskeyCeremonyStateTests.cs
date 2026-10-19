using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.InMemory.Common.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

/// <summary>
/// The state joining a passkey ceremony's two calls, through the real endpoints, the real data
/// protection key ring and the real spent-ceremony table — with verification that accepts every
/// ceremony (<see cref="AcceptingPasskeyHandler"/>), so a refusal can only be the state's.
/// </summary>
/// <remarks>
/// The replay these guard against: a captured state and credential presented again. For a synced
/// passkey, whose signature counter stays at zero, Identity's verification accepts the second
/// presentation as readily as the first, and the state is all that refuses it.
/// </remarks>
public sealed class PasskeyCeremonyStateTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _signInInvalid = "auth.passkeySignIn.invalid";

    private static int _acceptingClientCounter;

    [Fact]
    public async Task ReplayingASignIn_IsRefused_ThoughVerificationWouldAcceptIt()
    {
        var (_, _, session) = await SignInAsync();
        await SeedPasskeyAsync(session.UserId);
        using var host = AcceptingHost(out var ledger);
        using var client = AcceptingClient(host);

        string state = await BeginSignInAsync(client);

        using var first = await PostSignInAsync(client, session.UserId, state);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var replayed = await PostSignInAsync(client, session.UserId, state);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(replayed, TestToken)).Code.ShouldBe(_signInInvalid);

        ledger.Verified.Count.ShouldBe(
            1,
            "the replay must be refused before verification: the handler accepts everything, so had " +
            "it been asked a second time the refusal would not have been the state's.");
    }

    [Fact]
    public async Task ReplayingARegistration_IsRefused_ThoughVerificationWouldAcceptIt()
    {
        var (_, user, session) = await SignInAsync();
        using var host = AcceptingHost(out var ledger);
        using var client = AcceptingClient(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);

        string state = await BeginRegistrationAsync(client);

        using var first = await PostRegistrationAsync(client, session.UserId, state, user.Password);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var replayed = await PostRegistrationAsync(client, session.UserId, state, user.Password);
        replayed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ApiJson.ReadProblemAsync(replayed, TestToken);
        problem.Code.ShouldBe("request.validationFailed");
        problem.Body.ShouldContain("credential", Case.Sensitive);

        ledger.Verified.Count.ShouldBe(1);
    }

    /// <summary>
    /// The state is sealed: the client carries something it can neither read nor rewrite, and what
    /// reaches the handler is the handler's own state, unchanged.
    /// </summary>
    [Fact]
    public async Task AStateTheClientRewrote_IsRefusedBeforeVerification()
    {
        var (_, _, session) = await SignInAsync();
        await SeedPasskeyAsync(session.UserId);
        using var host = AcceptingHost(out var ledger);
        using var client = AcceptingClient(host);

        string state = await BeginSignInAsync(client);
        state.ShouldNotContain("challenge", Case.Insensitive);

        int middle = state.Length / 2;
        string rewritten = string.Concat(state.AsSpan(0, middle), [state[middle] == 'A' ? 'B' : 'A'], state.AsSpan(middle + 1));

        using var refused = await PostSignInAsync(client, session.UserId, rewritten);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refused, TestToken)).Code.ShouldBe(_signInInvalid);
        ledger.Verified.ShouldBeEmpty();

        // The untouched state still completes, so the refusal above was the rewrite's.
        using var accepted = await PostSignInAsync(client, session.UserId, state);
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        ledger.Verified.Single().ShouldContain("challenge", Case.Insensitive);
    }

    [Fact]
    public async Task AStatePastItsLifetime_IsRefusedBeforeVerification()
    {
        var (_, _, session) = await SignInAsync();
        await SeedPasskeyAsync(session.UserId);
        using var host = AcceptingHost(out var ledger);
        using var client = AcceptingClient(host);

        string state = await BeginSignInAsync(client);
        host.Services.GetRequiredService<FixedDateTimeProvider>().Advance(TimeSpan.FromMinutes(5));

        using var refused = await PostSignInAsync(client, session.UserId, state);
        refused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(refused, TestToken)).Code.ShouldBe(_signInInvalid);
        ledger.Verified.ShouldBeEmpty();
    }

    /// <summary>The two ceremonies seal under purposes of their own, so one's state is no use to the other.</summary>
    [Fact]
    public async Task ASignInState_CannotCompleteARegistration()
    {
        var (_, user, session) = await SignInAsync();
        using var host = AcceptingHost(out var ledger);
        using var client = AcceptingClient(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);

        string signInState = await BeginSignInAsync(client);

        using var refused = await PostRegistrationAsync(client, session.UserId, signInState, user.Password);
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(refused, TestToken)).Body.ShouldContain("credential", Case.Sensitive);
        ledger.Verified.ShouldBeEmpty();
    }

    /// <summary>
    /// A host whose passkey verification accepts every ceremony, its clock placed where the suite's
    /// is — a derived host has a clock of its own, and the protector reads the machine's beside it.
    /// </summary>
    private WebApplicationFactory<Program> AcceptingHost(out AcceptingPasskeyHandler.Ledger ledger)
    {
        var recorded = new AcceptingPasskeyHandler.Ledger();
        ledger = recorded;

        var host = Fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(recorded);
            services.AddScoped<IPasskeyHandler<AppUser>, AcceptingPasskeyHandler>();
        }));

        host.Services.GetRequiredService<FixedDateTimeProvider>().Set(Clock.UtcNow);

        return host;
    }

    /// <summary>A client of the derived host with an address of its own, as <see cref="IntegrationTestBase.CreateClient"/> hands out.</summary>
    private static HttpClient AcceptingClient(WebApplicationFactory<Program> host)
    {
        int index = Interlocked.Increment(ref _acceptingClientCounter);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, $"10.251.0.{(index % 250) + 1}");

        return client;
    }

    private async Task SeedPasskeyAsync(Guid userId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"No account has the id '{userId}'.");

        (await userManager.AddOrUpdatePasskeyAsync(user, AcceptingPasskeyHandler.APasskey(AcceptingPasskeyHandler.CredentialId)))
            .Succeeded.ShouldBeTrue();
    }

    private static async Task<string> BeginSignInAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            new Uri($"{AuthRoute}/passkeys/sign-in/options", UriKind.Relative), content: null, TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ApiJson.ReadAsync<PasskeySignInOptionsResponse>(response, TestToken)).AuthenticationState;
    }

    private static async Task<string> BeginRegistrationAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            new Uri($"{AuthRoute}/passkeys/register/options", UriKind.Relative), content: null, TestToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ApiJson.ReadAsync<PasskeyRegistrationOptionsResponse>(response, TestToken)).RegistrationState;
    }

    /// <summary>The credential is the account's id: what <see cref="AcceptingPasskeyHandler"/> answers for.</summary>
    private static Task<HttpResponseMessage> PostSignInAsync(HttpClient client, Guid userId, string state) =>
        client.PostAsJsonAsync(
            $"{AuthRoute}/passkeys/sign-in",
            new PasskeySignInRequest(userId.ToString(), state),
            TestToken);

    private static Task<HttpResponseMessage> PostRegistrationAsync(
        HttpClient client,
        Guid userId,
        string state,
        string password) =>
        client.PostAsJsonAsync(
            $"{AuthRoute}/passkeys/register",
            new CompletePasskeyRegistrationRequest(userId.ToString(), state, "A key", password),
            TestToken);
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Auth.Contracts.Requests;
using AppTemplate.Api.Features.Auth.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Auth.Features.AccessKeys.UseCases.Commands.AuthenticateAccessKey;
using AppTemplate.Application.Core.Common.Results;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Auth;

public sealed class AccessKeyManagementTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string _accessKeysRoute = AuthRoute + "/access-keys";

    private static readonly string[] _read = ["read"];

    [Fact]
    public async Task ACreatedKey_IsShownOnce_ThenListedWithoutItsSecret()
    {
        var (client, user, _) = await SignInAsync();

        using var created = await CreateAsync(client, new CreateAccessKeyRequest("CI", _read, null, user.Password));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        created.Headers.CacheControl!.NoStore.ShouldBeTrue();
        created.Headers.Location!.ToString().ShouldEndWith(_accessKeysRoute);
        var key = await ApiJson.ReadAsync<CreatedAccessKeyResponse>(created, TestToken);
        key.Key.ShouldStartWith("ak_");
        key.Prefix.ShouldBe(key.Key[..key.Prefix.Length]);
        key.Scopes.ShouldBe(_read);

        using var listed = await client.GetAsync(new Uri(_accessKeysRoute, UriKind.Relative), TestToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await listed.Content.ReadAsStringAsync(TestToken);
        body.ShouldNotContain(key.Key[key.Prefix.Length..], Case.Sensitive, "the list never carries the secret");
        var keys = (await ApiJson.ReadAsync<AccessKeysResponse>(listed, TestToken)).AccessKeys;
        keys.ShouldHaveSingleItem().ShouldBe(
            new AccessKeyResponse(key.Id, "CI", key.Prefix, keys[0].Scopes, key.CreatedAt, null, null));
        keys[0].Scopes.ShouldBe(_read);
    }

    [Fact]
    public async Task CreatingAKey_WithoutTheRightPassword_IsRefused_AndCreatesNothing()
    {
        var (client, user, _) = await SignInAsync();

        using (var wrong = await CreateAsync(client, new CreateAccessKeyRequest("CI", _read, null, "not it")))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiJson.ReadProblemAsync(wrong, TestToken)).Body.ShouldContain("currentPassword");
        }

        using (var missing = await CreateAsync(client, new CreateAccessKeyRequest("CI", _read, null, null)))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ListAsync(client)).ShouldBeEmpty();

        using var right = await CreateAsync(client, new CreateAccessKeyRequest("CI", _read, null, user.Password));
        right.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task AnExpiryInThePast_OrAnUnknownScope_IsRefused()
    {
        var (client, user, _) = await SignInAsync();

        using (var past = await CreateAsync(
            client, new CreateAccessKeyRequest("CI", _read, Clock.UtcNow.AddMinutes(-1), user.Password)))
        {
            past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await ApiJson.ReadProblemAsync(past, TestToken)).Body.ShouldContain("expiresAt");
        }

        using (var admin = await CreateAsync(client, new CreateAccessKeyRequest("CI", ["admin"], null, user.Password)))
        {
            admin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        (await ListAsync(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EveryRoute_RequiresASignedInCaller()
    {
        var anonymous = CreateClient();

        using var created = await CreateAsync(anonymous, new CreateAccessKeyRequest("CI", _read, null, "whatever"));
        using var listed = await anonymous.GetAsync(new Uri(_accessKeysRoute, UriKind.Relative), TestToken);
        using var revoked = await RevokeAsync(anonymous, Guid.CreateVersion7());

        foreach (var response in new[] { created, listed, revoked })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("auth.required");
        }
    }

    [Fact]
    public async Task AnotherCallersKey_AnswersNotFound_AndStaysLive()
    {
        var (owner, ownerUser, _) = await SignInAsync();
        var (other, _, _) = await SignInAsync("other");
        var key = await CreateKeyAsync(owner, ownerUser.Password);

        using (var foreign = await RevokeAsync(other, key.Id))
        {
            foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ApiJson.ReadProblemAsync(foreign, TestToken)).Code.ShouldBe("auth.accessKey.notFound");
        }

        using (var unknown = await RevokeAsync(owner, Guid.CreateVersion7()))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await ListAsync(owner)).ShouldHaveSingleItem().Id.ShouldBe(key.Id);
        (await AuthenticateAsync(key.Key)).IsSuccess.ShouldBeTrue();

        using (var revoked = await RevokeAsync(owner, key.Id))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var again = await RevokeAsync(owner, key.Id))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ApiJson.ReadProblemAsync(again, TestToken)).Code.ShouldBe("auth.accessKey.notFound");
        }

        (await ListAsync(owner)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ACreatedKey_StandsForItsOwnerThroughTheUseCase_UntilRevoked()
    {
        var (client, user, session) = await SignInAsync();
        var key = await CreateKeyAsync(client, user.Password);

        var caller = await AuthenticateAsync(key.Key);
        caller.IsSuccess.ShouldBeTrue();
        caller.Value.UserId.ShouldBe(session.UserId);
        caller.Value.AccessKeyId.ShouldBe(key.Id);
        caller.Value.Scopes.ShouldBe(_read);
        (await ListAsync(client)).ShouldHaveSingleItem().LastUsedAt.ShouldBe(Clock.UtcNow);

        using (var revoked = await RevokeAsync(client, key.Id))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await AuthenticateAsync(key.Key)).Error!.Code.ShouldBe("auth.accessKey.invalid");
    }

    [Fact]
    public async Task ThisApi_NeverAcceptsAnAccessKeyAsABearerToken()
    {
        var (client, user, _) = await SignInAsync();
        var key = await CreateKeyAsync(client, user.Password);
        (await AuthenticateAsync(key.Key)).IsSuccess.ShouldBeTrue();

        var holder = CreateClient();
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Key);

        using var me = await holder.GetAsync(new Uri($"{AuthRoute}/me", UriKind.Relative), TestToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ApiJson.ReadProblemAsync(me, TestToken)).Code.ShouldBe("auth.required");

        using var keys = await holder.GetAsync(new Uri(_accessKeysRoute, UriKind.Relative), TestToken);
        keys.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<CreatedAccessKeyResponse> CreateKeyAsync(HttpClient client, string password)
    {
        using var created = await CreateAsync(client, new CreateAccessKeyRequest("CI", _read, null, password));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        return await ApiJson.ReadAsync<CreatedAccessKeyResponse>(created, TestToken);
    }

    private async Task<Result<AccessKeyCaller>> AuthenticateAsync(string key)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IAuthenticateAccessKeyUseCase>()
            .ExecuteAsync(new AuthenticateAccessKeyCommand(key), TestToken);
    }

    private static async Task<IReadOnlyList<AccessKeyResponse>> ListAsync(HttpClient client)
    {
        using var listed = await client.GetAsync(new Uri(_accessKeysRoute, UriKind.Relative), TestToken);
        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await ApiJson.ReadAsync<AccessKeysResponse>(listed, TestToken)).AccessKeys;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, CreateAccessKeyRequest request) =>
        client.PostAsJsonAsync(_accessKeysRoute, request, TestToken);

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid accessKeyId) =>
        client.DeleteAsync(new Uri($"{_accessKeysRoute}/{accessKeyId}", UriKind.Relative), TestToken);
}

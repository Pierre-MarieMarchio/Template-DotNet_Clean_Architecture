using System.Net;
using System.Net.Http.Headers;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// The other half of the decision recorded in ADR 0013: what a deployment gets when it turns
/// <c>Concurrency:IfMatch</c> up to <c>Required</c>.
/// </summary>
/// <remarks>
/// <para>
/// A second host is built for these, because the setting is read at composition time and the shared
/// fixture's host is the one every other test needs at the shipped default. The account, the list and
/// the token come from the shared host: both hosts read the same database and the same signing key
/// from the environment, so a token minted by one is accepted by the other.
/// </para>
/// <para>
/// Two tests, not one. "An unconditional write is refused" is worth nothing without "a conditional
/// one still works" beside it — a host that answered 428 to everything would satisfy the first.
/// </para>
/// </remarks>
public sealed class IfMatchRequiredTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUnconditionalWrite_IsRefusedWith428()
    {
        var (client, _, session) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var host = StrictHost();
        using var strict = ClientOf(host, session.Tokens.AccessToken);

        using var response = await RenameAsync(strict, listId, "Renamed unconditionally");

        response.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionRequired,
            "with If-Match required, a write that names no version must not be applied: " +
            await response.Content.ReadAsStringAsync(TestToken));

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Status.ShouldBe((int)HttpStatusCode.PreconditionRequired, problem.Body);
        problem.Code.ShouldBe("precondition.required", problem.Body);
        problem.Detail.ShouldNotBeNull(problem.Body);
        problem.Detail.ShouldContain("If-Match");

        SecurityHeaderAssertions.AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AConditionalWrite_StillSucceeds()
    {
        var (client, _, session) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");
        string tag = await ReadETagAsync(client, listId);

        using var host = StrictHost();
        using var strict = ClientOf(host, session.Tokens.AccessToken);

        using var response = await RenameAsync(strict, listId, "Renamed conditionally", tag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Reads are never conditional-only: a client has to be able to obtain a validator.</summary>
    [Fact]
    public async Task AReadWithoutAnIfMatch_IsStillServed()
    {
        var (client, _, session) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Groceries");

        using var host = StrictHost();
        using var strict = ClientOf(host, session.Tokens.AccessToken);

        using var response = await strict.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.ETag.ShouldNotBeNull();
    }

    private WebApplicationFactory<ApiControllerBase> StrictHost() =>
        Fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{ConcurrencyOptions.SectionName}:{nameof(ConcurrencyOptions.IfMatch)}",
            nameof(IfMatchRequirement.Required)));

    private static HttpClient ClientOf(WebApplicationFactory<ApiControllerBase> host, string accessToken)
    {
        var client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Its own rate-limit partition, out of the range CreateClient hands out.
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "10.250.0.1");

        return client;
    }
}

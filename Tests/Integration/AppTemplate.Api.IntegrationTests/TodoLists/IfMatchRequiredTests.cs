using System.Net;
using System.Net.Http.Headers;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

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

    private WebApplicationFactory<Program> StrictHost() =>
        Fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{ConcurrencyOptions.SectionName}:{nameof(ConcurrencyOptions.IfMatch)}",
            nameof(IfMatchRequirement.Required)));

    private static HttpClient ClientOf(WebApplicationFactory<Program> host, string accessToken)
    {
        var client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "10.250.0.1");

        return client;
    }
}

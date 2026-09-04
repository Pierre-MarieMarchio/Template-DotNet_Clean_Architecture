using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Http;

/// <summary>
/// Kestrel's shipped default accepts a 30 MB request body, which is a free denial-of-service against
/// a JSON API whose largest legitimate body is a few kilobytes.
/// </summary>
/// <remarks>
/// A second host lowers <c>RequestLimits:MaxRequestBodyBytes</c> to the validator's own floor, 1024
/// bytes, so the "over the limit" case needs no 64 KB payload to reach it — the same reasoning
/// <see cref="TodoLists.IfMatchRequiredTests"/> gives for building a second host rather than
/// reconfiguring the shared one: the setting is read once, at composition time.
/// </remarks>
public sealed class RequestBodySizeLimitTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const long _loweredLimit = 1024;

    [Fact]
    public async Task ABodyOverTheLimit_IsRefusedWith413()
    {
        var (_, _, session) = await SignInAsync();

        using var host = HostWithLoweredLimit();
        using var limited = ClientOf(host, session.Tokens.AccessToken);

        // Far larger than a business-valid name, but that is the point: the middleware runs before
        // model binding or validation, so an oversized body never reaches either.
        string oversizedName = new string('a', (int)_loweredLimit + 500);

        using var response = await limited.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest(oversizedName),
            TestToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.RequestEntityTooLarge,
            "a body over the configured limit must be refused before it is read: " +
            await response.Content.ReadAsStringAsync(TestToken));

        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Status.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge, problem.Body);
        problem.Code.ShouldBe(
            "request.tooLarge",
            "clients branch on the code, and 413 has exactly one meaning on this API. " + problem.Body);
    }

    [Fact]
    public async Task ABodyUnderTheLimit_IsProcessedNormally()
    {
        var (_, _, session) = await SignInAsync();

        using var host = HostWithLoweredLimit();
        using var limited = ClientOf(host, session.Tokens.AccessToken);

        using var response = await limited.PostAsJsonAsync(
            TodoListsRoute,
            new CreateTodoListRequest("Groceries"),
            TestToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            "a body well under the limit must be processed exactly as if there were no limit at all: " +
            await response.Content.ReadAsStringAsync(TestToken));
    }

    private WebApplicationFactory<ApiControllerBase> HostWithLoweredLimit() =>
        Fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{RequestLimitsOptions.SectionName}:{nameof(RequestLimitsOptions.MaxRequestBodyBytes)}",
            _loweredLimit.ToString(CultureInfo.InvariantCulture)));

    private static HttpClient ClientOf(WebApplicationFactory<ApiControllerBase> host, string accessToken)
    {
        var client = host.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // Its own rate-limit partition, out of the range CreateClient hands out.
        client.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "10.250.2.1");

        return client;
    }
}

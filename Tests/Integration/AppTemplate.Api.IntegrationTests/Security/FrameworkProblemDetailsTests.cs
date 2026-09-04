using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The failures the application never decides on still have to look like the ones it does.
/// </summary>
/// <remarks>
/// Some requests are answered before any of this API's code runs: a body that is not JSON, a route
/// segment that fails its <c>:guid</c> constraint, a verb no action accepts, a media type nothing
/// reads. Those come from the framework, not from an <c>Error</c>, so they never pass through
/// <c>ErrorResults</c> and do not inherit its <c>code</c>. The API's contract is that a client
/// branches on <c>code</c> and never on prose — which is worth nothing on precisely the inputs a
/// client is most likely to get wrong by accident.
/// </remarks>
public sealed class FrameworkProblemDetailsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ABodyThatIsNotJson_IsAProblemDocumentWithACode()
    {
        var (client, _, _) = await SignInAsync();

        using var content = new StringContent("{ not json", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.PostAsync(
            new Uri(TodoListsRoute, UriKind.Relative),
            content,
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await AssertCarriesACodeAsync(response);
    }

    [Fact]
    public async Task ARouteSegmentFailingItsConstraint_IsAProblemDocumentWithACode()
    {
        var (client, _, _) = await SignInAsync();

        // The route declares {todoListId:guid}; "not-a-guid" never reaches an action.
        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/not-a-guid", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertCarriesACodeAsync(response);
    }

    [Fact]
    public async Task AVerbNoActionAccepts_IsAProblemDocumentWithACode()
    {
        var (client, _, _) = await SignInAsync();

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            new Uri(TodoListsRoute, UriKind.Relative));

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        await AssertCarriesACodeAsync(response);
    }

    [Fact]
    public async Task AMediaTypeNothingReads_IsAProblemDocumentWithACode()
    {
        var (client, _, _) = await SignInAsync();

        using var content = new StringContent("name=Groceries", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var response = await client.PostAsync(
            new Uri(TodoListsRoute, UriKind.Relative),
            content,
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        await AssertCarriesACodeAsync(response);
    }

    /// <summary>
    /// A code the application authored must survive: the default must fill the field in, never
    /// overwrite a more specific value that <c>ErrorResults</c> already set.
    /// </summary>
    [Fact]
    public async Task AnErrorTheApplicationAuthored_KeepsItsOwnCode()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{Guid.NewGuid()}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Code.ShouldBe("todoList.notFound", problem.Body);
    }

    private static async Task AssertCarriesACodeAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Code.ShouldNotBeNullOrWhiteSpace(problem.Body);
        problem.Code.ShouldContain(".", Case.Sensitive, problem.Body);
        SecurityHeaderAssertions.AssertSecurityHeaders(response);
    }
}

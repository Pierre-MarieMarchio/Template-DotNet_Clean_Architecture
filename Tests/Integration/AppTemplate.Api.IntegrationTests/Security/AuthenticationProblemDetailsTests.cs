using System.Net;
using System.Text.Json;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Security;

/// <summary>
/// The two most common failures this API can produce — 401 and 403 — look like every other failure.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards: a bearer handler left to its own devices answers a challenge and a forbidden
/// in a shape of its own — <c>application/json</c>, an English <c>message</c>, no <c>code</c> — while
/// every other failure in the API is <c>application/problem+json</c> with a stable machine-readable
/// <c>code</c>. A client then has to special-case the two responses it sees most often, with nothing but
/// prose to branch on, so "session expired, refresh and retry" becomes indistinguishable from anything
/// else without string matching.
/// </para>
/// <para>
/// The 403 is asserted by invoking the configured handler directly. That is not a shortcut: this API's
/// fallback policy only requires an authenticated user, so an authenticated caller passes every endpoint
/// and there is no route that can be made to answer 403 without inventing one. Testing the configuration
/// that <em>would</em> render it is the honest alternative to a test-only endpoint, and it still runs
/// against the options the real host composed.
/// </para>
/// </remarks>
public sealed class AuthenticationProblemDetailsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUnauthenticatedRequest_AnswersAProblemDocumentWithAStableCode()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        problem.Title.ShouldBe("Unauthorized");
        problem.Code.ShouldBe("auth.required", problem.Body);
        problem.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AGarbageToken_AnswersTheSameProblemDocument()
    {
        var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, TodoListsRoute);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-token");

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        // Identical to the no-token case, deliberately. "Expired", "wrong signature" and "revoked security
        // stamp" are all facts about the system's state that an unauthenticated caller has no business
        // learning.
        problem.Code.ShouldBe("auth.required", problem.Body);
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldNotContain("signature");
        problem.Detail.ShouldNotContain("expired");
    }

    /// <summary>
    /// The 401 body carries a trace identifier, like every other problem document, so a support request can
    /// be tied to the log line without the response saying anything about why the token was refused.
    /// </summary>
    [Fact]
    public async Task TheProblemDocument_CarriesATraceIdentifier()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(TodoListsRoute, UriKind.Relative), TestToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

        document.RootElement.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The forbidden path, driven through the events the real host configured.
    /// </summary>
    [Fact]
    public async Task TheForbiddenHandler_WritesAProblemDocumentWithItsOwnCode()
    {
        var options = Fixture.Factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Events.ShouldNotBeNull();

        var httpContext = new DefaultHttpContext { RequestServices = Fixture.Factory.Services };
        using var body = new MemoryStream();
        httpContext.Response.Body = body;

        var scheme = new AuthenticationScheme(
            JwtBearerDefaults.AuthenticationScheme,
            displayName: null,
            handlerType: typeof(JwtBearerHandler));

        await options.Events.Forbidden(new ForbiddenContext(httpContext, scheme, options));

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        httpContext.Response.ContentType.ShouldStartWith("application/problem+json");

        body.Position = 0;
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: TestToken);
        var root = document.RootElement;

        root.GetProperty("status").GetInt32().ShouldBe(StatusCodes.Status403Forbidden);
        root.GetProperty("title").GetString().ShouldBe("Forbidden");

        // A different code from the 401, because the two call for different client behaviour: refresh and
        // retry versus stop and tell the user.
        root.GetProperty("code").GetString().ShouldBe("auth.forbidden");
        root.GetProperty("code").GetString().ShouldNotBe("auth.required");
    }
}

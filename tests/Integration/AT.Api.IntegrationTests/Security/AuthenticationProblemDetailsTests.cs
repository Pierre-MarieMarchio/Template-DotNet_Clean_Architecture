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

public sealed class AuthenticationProblemDetailsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnUnauthenticatedRequest_AnswersAProblemDocumentWithAStableCode()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

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

        using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer not-a-token");

        using var response = await client.SendAsync(request, TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);

        problem.Code.ShouldBe("auth.required", problem.Body);
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldNotContain("signature");
        problem.Detail.ShouldNotContain("expired");
    }

    [Fact]
    public async Task TheProblemDocument_CarriesATraceIdentifier()
    {
        var client = CreateClient();

        using var response = await client.GetAsync(new Uri(ProtectedRoute, UriKind.Relative), TestToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestToken));

        document.RootElement.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrWhiteSpace();
    }

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

        root.GetProperty("code").GetString().ShouldBe("auth.forbidden");
        root.GetProperty("code").GetString().ShouldNotBe("auth.required");
    }
}

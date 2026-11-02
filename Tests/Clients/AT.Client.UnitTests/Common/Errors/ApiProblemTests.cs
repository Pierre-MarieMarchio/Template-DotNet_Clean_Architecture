using System.Net;
using AppTemplate.Client.Common.Errors;
using AppTemplate.Client.Generated.Models;
using AppTemplate.Client.UnitTests.TestSupport;
using Microsoft.Kiota.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Client.UnitTests.Common.Errors;

public sealed class ApiProblemTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProblemAnswer_GivesItsCode()
    {
        var error = await FailureOf(
            StubApi.Problem(
                HttpStatusCode.Forbidden,
                """{"type":"about:blank","title":"Forbidden","status":403,"detail":"Administrators only.","code":"auth.forbidden","traceId":"t-1"}"""),
            client => client.Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation));

        var problem = ApiProblem.From(error);

        problem.Status.ShouldBe(403);
        problem.Code.ShouldBe("auth.forbidden");
        problem.Detail.ShouldBe("Administrators only.");
        problem.TraceId.ShouldBe("t-1");
        problem.FieldErrors.ShouldBeEmpty();
    }

    //#if (localAuth)
    [Fact]
    public async Task AValidationAnswer_GivesItsFieldErrors()
    {
        var error = await FailureOf(
            StubApi.Problem(
                HttpStatusCode.BadRequest,
                """{"type":"about:blank","status":400,"code":"validation.failed","traceId":"t-2","errors":{"email":["Email is required.","Email is malformed."],"password":["Password is required."]}}"""),
            client => client.Api.V1.Auth.Login.PostAsync(new LoginRequest(), cancellationToken: Cancellation));

        var problem = ApiProblem.From(error);

        problem.Code.ShouldBe("validation.failed");
        problem.ErrorsFor("email").ShouldBe(["Email is required.", "Email is malformed."]);
        problem.ErrorsFor("password").ShouldBe(["Password is required."]);
        problem.ErrorsFor("name").ShouldBeEmpty();
    }
    //#endif

    [Fact]
    public async Task AnAnswerThatIsNoProblem_IsUnreadable()
    {
        var error = await FailureOf(
            StubApi.Json(HttpStatusCode.Forbidden, "<html>Blocked by the proxy</html>", "text/html"),
            client => client.Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation));

        var problem = ApiProblem.From(error);

        problem.Status.ShouldBe(403);
        problem.Code.ShouldBe(ApiProblem.UnreadableCode);
    }

    [Fact]
    public async Task AProblemWithoutACode_IsUnreadable()
    {
        var error = await FailureOf(
            StubApi.Problem(HttpStatusCode.Forbidden, """{"type":"about:blank","status":403}"""),
            client => client.Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation));

        ApiProblem.From(error).Code.ShouldBe(ApiProblem.UnreadableCode);
    }

    [Fact]
    public async Task AStatusTheContractDoesNotDeclare_IsUnreadable()
    {
        var error = await FailureOf(
            StubApi.Problem(HttpStatusCode.NotFound, """{"type":"about:blank","status":404,"code":"route.notFound","traceId":"t-3"}"""),
            client => client.Api.V1.Maintenance.IdempotencyKeys.Expired.DeleteAsync(cancellationToken: Cancellation));

        var problem = ApiProblem.From(error);

        problem.Status.ShouldBe(404);
        problem.Code.ShouldBe(ApiProblem.UnreadableCode);
    }

    private static async Task<ApiException> FailureOf(
        HttpResponseMessage answer,
        Func<Generated.ApiClient, Task> call)
    {
        using var harness = new ClientHarness(new StubApi(_ => Task.FromResult(answer)));

        return await Should.ThrowAsync<ApiException>(() => call(harness.Client));
    }
}

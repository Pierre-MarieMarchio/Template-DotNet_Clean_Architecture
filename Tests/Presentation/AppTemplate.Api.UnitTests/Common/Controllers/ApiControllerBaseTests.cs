using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Concurrency;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Controllers;

public sealed class ApiControllerBaseTests
{
    [Fact]
    public void OkOrProblem_Versioned_PublishesTheEtag_BeforeAnswering200()
    {
        var controller = AController();
        var result = controller.CallOkOrProblem(Result.Success(new Versioned<string>("value", 3)));

        result.Result.ShouldBeOfType<OkObjectResult>();
        controller.Response.Headers.ETag.ToString().ShouldBe(EntityTagValue.From(3));
    }

    /// <summary>
    /// RFC 9110 requires a 304 to carry the validator it is refusing to resend the body for — the
    /// header has to be on the response before the 304-or-200 decision is made, not after.
    /// </summary>
    [Fact]
    public void OkOrProblem_Versioned_PublishesTheEtag_EvenWhenAnsweringNotModified()
    {
        string tag = EntityTagValue.From(5);
        var controller = AController();
        controller.Request.Headers.IfNoneMatch = tag;

        var result = controller.CallOkOrProblem(Result.Success(new Versioned<string>("value", 5)));

        result.Result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        controller.Response.Headers.ETag.ToString().ShouldBe(tag);
    }

    [Fact]
    public void OkOrProblem_Versioned_MapsAFailure_WithoutTouchingTheEtagHeader()
    {
        var controller = AController();

        var result = controller.CallOkOrProblem(Result.Failure<Versioned<string>>(SomeError));

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        controller.Response.Headers.ETag.Count.ShouldBe(0);
    }

    [Fact]
    public void UpdatedOrProblem_PublishesTheEtag_AndAnswers200()
    {
        var controller = AController();

        var result = controller.CallUpdatedOrProblem(Result.Success(new Versioned<string>("value", 9)));

        result.Result.ShouldBeOfType<OkObjectResult>();
        controller.Response.Headers.ETag.ToString().ShouldBe(EntityTagValue.From(9));
    }

    [Fact]
    public void CreatedOrProblem_Versioned_PublishesTheEtag_AndBuildsTheRoute()
    {
        var controller = AController();

        var result = controller.CallCreatedOrProblem(
            Result.Success(new Versioned<string>("the-id", 1)),
            routeValues: value => new { id = value });

        var created = result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        created.RouteValues!["id"].ShouldBe("the-id");
        controller.Response.Headers.ETag.ToString().ShouldBe(EntityTagValue.From(1));
    }

    /// <summary>
    /// The whole point of taking a <see cref="Func{T,TResult}"/> rather than a value: a failed result
    /// has no value to build a route from, so nothing may evaluate it.
    /// </summary>
    [Fact]
    public void CreatedOrProblem_Versioned_NeverInvokesRouteValues_OnFailure()
    {
        var controller = AController();
        bool invoked = false;

        var result = controller.CallCreatedOrProblem(
            Result.Failure<Versioned<string>>(SomeError),
            routeValues: value =>
            {
                invoked = true;
                return new { id = value };
            });

        invoked.ShouldBeFalse();
        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ReadPrecondition_IsNull_WhenNoHeaderIsSent_AndIfMatchIsOptional()
    {
        var controller = AController();

        var refusal = controller.CallReadPrecondition(out var precondition, out bool requiresExistence);

        refusal.ShouldBeNull();
        precondition.ShouldBeNull();
        requiresExistence.ShouldBeFalse();
    }

    [Fact]
    public void ReadPrecondition_Refuses428_WhenNoHeaderIsSent_AndIfMatchIsRequired()
    {
        var controller = AController(ifMatchRequirement: IfMatchRequirement.Required);

        var refusal = controller.CallReadPrecondition(out _, out _);

        refusal.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status428PreconditionRequired);
    }

    [Fact]
    public void ReadPrecondition_Refuses400_ForAMalformedHeader()
    {
        var controller = AController();
        controller.Request.Headers.IfMatch = "not-an-etag";

        var refusal = controller.CallReadPrecondition(out _, out _);

        refusal.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ReadPrecondition_RequiresExistence_ForAWildcard()
    {
        var controller = AController();
        controller.Request.Headers.IfMatch = "*";

        var refusal = controller.CallReadPrecondition(out var precondition, out bool requiresExistence);

        refusal.ShouldBeNull();
        precondition.ShouldBeNull();
        requiresExistence.ShouldBeTrue();
    }

    [Fact]
    public void ReadPrecondition_NamesTheVersion_ForAKnownTag()
    {
        var controller = AController();
        controller.Request.Headers.IfMatch = EntityTagValue.From(4);

        controller.CallReadPrecondition(out var precondition, out bool requiresExistence);

        requiresExistence.ShouldBeFalse();
        precondition!.IsSatisfiedBy(4).ShouldBeTrue();
    }

    [Fact]
    public void RequiringExistence_TurnsANotFound_IntoAPreconditionFailed_WhenExistenceWasRequired()
    {
        var result = TestController.CallRequiringExistence(requiresExistence: true, Result.Failure(SomeError));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Fact]
    public void RequiringExistence_LeavesANotFound_UntouchedWhenExistenceWasNotRequired()
    {
        var result = TestController.CallRequiringExistence(requiresExistence: false, Result.Failure(SomeError));

        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void RequiringExistence_LeavesAnUnrelatedFailure_Untouched()
    {
        var conflict = Error.Conflict("todoList.duplicate", "already exists");

        var result = TestController.CallRequiringExistence(requiresExistence: true, Result.Failure(conflict));

        result.Error.ShouldBe(conflict);
    }

    private static readonly Error SomeError = Error.NotFound("todoList.notFound", "gone");

    private static TestController AController(IfMatchRequirement ifMatchRequirement = IfMatchRequirement.Optional)
    {
        var controller = new TestController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = HttpContextFactory.Create(ifMatchRequirement: ifMatchRequirement),
            },
        };

        return controller;
    }

    private sealed class TestController : ApiControllerBase
    {
        public ActionResult<string> CallOkOrProblem(Result<Versioned<string>> result) => OkOrProblem(result);

        public ActionResult<string> CallUpdatedOrProblem(Result<Versioned<string>> result) => UpdatedOrProblem(result);

        public ActionResult<string> CallCreatedOrProblem(
            Result<Versioned<string>> result,
            Func<string, object> routeValues) =>
            CreatedOrProblem(result, "SomeRoute", routeValues);

        public ActionResult? CallReadPrecondition(out VersionPrecondition? precondition, out bool requiresExistence) =>
            ReadPrecondition(out precondition, out requiresExistence);

        public static Result CallRequiringExistence(bool requiresExistence, Result result) =>
            RequiringExistence(requiresExistence, result);
    }
}

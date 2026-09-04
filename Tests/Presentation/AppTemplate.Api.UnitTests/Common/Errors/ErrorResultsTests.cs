using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

public sealed class ErrorResultsTests
{
    [Fact]
    public void ToActionResult_MapsTheStatusFromTheErrorType()
    {
        var result = Error.NotFound("todoList.notFound", "gone").ToActionResult(HttpContextFactory.Create());

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToActionResult_CarriesTheErrorCode_AsAnExtension()
    {
        var result = Error.Conflict("todoList.duplicate", "already exists").ToActionResult(HttpContextFactory.Create());

        var problem = ((ObjectResult)result).Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions["code"].ShouldBe("todoList.duplicate");
    }

    [Fact]
    public void ToActionResult_SetsTraceId_WhenGivenAnHttpContext()
    {
        var httpContext = HttpContextFactory.Create();
        var result = Error.NotFound("todoList.notFound", "gone").ToActionResult(httpContext);

        var problem = ((ObjectResult)result).Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions["traceId"].ShouldBe(httpContext.TraceIdentifier);
    }

    /// <summary>
    /// Without a request to normalise against, there is no traceId to read — the contextless overload
    /// exists only for callers this pass could not migrate, and must not throw for lack of one.
    /// </summary>
    [Fact]
    public void ToActionResult_WithoutAnHttpContext_CarriesNoTraceIdButStillCarriesACode()
    {
        var result = Error.NotFound("todoList.notFound", "gone").ToActionResult();

        var problem = ((ObjectResult)result).Value.ShouldBeOfType<ProblemDetails>();
        problem.Extensions.ContainsKey("traceId").ShouldBeFalse();
        problem.Extensions["code"].ShouldBe("todoList.notFound");
    }

    /// <summary>
    /// The one distinction a client should never have to make: a validation failure answers exactly
    /// the same graph whether the application authored it or MVC's own model binder did.
    /// </summary>
    [Fact]
    public void ToActionResult_ProducesAValidationProblemDetails_WhenTheErrorCarriesFieldDetails()
    {
        var details = new Dictionary<string, IReadOnlyList<string>> { ["name"] = ["is required"] };
        var error = Error.Validation("request.validationFailed", "invalid", details);

        var result = error.ToActionResult(HttpContextFactory.Create());

        var problem = ((ObjectResult)result).Value.ShouldBeOfType<ValidationProblemDetails>();
        problem.Errors["name"].ShouldBe(["is required"]);
        problem.Status.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToActionResult_ProducesABareProblemDetails_WhenTheErrorCarriesNoFieldDetails()
    {
        var result = Error.Validation("precondition.malformed", "bad header").ToActionResult(HttpContextFactory.Create());

        ((ObjectResult)result).Value.ShouldBeOfType<ProblemDetails>();
    }

    [Fact]
    public void ToActionResult_SetsTheContentType_ToProblemJson()
    {
        var result = Error.NotFound("todoList.notFound", "gone").ToActionResult(HttpContextFactory.Create());

        ((ObjectResult)result).ContentTypes.ShouldContain("application/problem+json");
    }

    [Theory]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.TooManyRequests, StatusCodes.Status429TooManyRequests)]
    [InlineData(ErrorType.PreconditionFailed, StatusCodes.Status412PreconditionFailed)]
    [InlineData(ErrorType.PreconditionRequired, StatusCodes.Status428PreconditionRequired)]
    public void ToActionResult_MapsEveryErrorType_ToItsOwnStatus(ErrorType type, int expectedStatus)
    {
        var error = type switch
        {
            ErrorType.Unauthorized => Error.Unauthorized("code", "message"),
            ErrorType.Forbidden => Error.Forbidden("code", "message"),
            ErrorType.Conflict => Error.Conflict("code", "message"),
            ErrorType.TooManyRequests => new Error("code", "message", ErrorType.TooManyRequests),
            ErrorType.PreconditionFailed => Error.PreconditionFailed("code", "message"),
            ErrorType.PreconditionRequired => Error.PreconditionRequired("code", "message"),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        var result = error.ToActionResult(HttpContextFactory.Create());

        ((ObjectResult)result).StatusCode.ShouldBe(expectedStatus);
    }
}

using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

public sealed class ProblemDetailsDefaultsTests
{
    [Fact]
    public void Normalise_SetsTraceId_FromTheHttpContext()
    {
        var httpContext = HttpContextFactory.Create();
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Extensions["traceId"].ShouldBe(httpContext.TraceIdentifier);
    }

    [Fact]
    public void Normalise_NeverOverwritesAnAlreadyPresentTraceId()
    {
        var httpContext = HttpContextFactory.Create();
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };
        problem.Extensions["traceId"] = "caller-supplied";

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Extensions["traceId"].ShouldBe("caller-supplied");
    }

    [Fact]
    public void Normalise_DerivesCodeFromStatus_WhenNoneIsPresent()
    {
        var httpContext = HttpContextFactory.Create();
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Extensions["code"].ShouldBe(ProblemDetailsDefaults.CodeFor(StatusCodes.Status404NotFound));
    }

    [Fact]
    public void Normalise_NeverOverwritesAnAlreadyPresentCode()
    {
        var httpContext = HttpContextFactory.Create();
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };
        problem.Extensions["code"] = "todoList.notFound";

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Extensions["code"].ShouldBe("todoList.notFound");
    }

    [Fact]
    public void Normalise_DerivesTypeFromTheCode_UsingTheConfiguredBaseUri()
    {
        var httpContext = HttpContextFactory.Create(problemTypeBaseUri: "https://example.org/problems");
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound };
        problem.Extensions["code"] = "todoList.notFound";

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Type.ShouldBe("https://example.org/problems/todoList.notFound");
    }

    [Fact]
    public void Normalise_NeverOverwritesAnAlreadyPresentType()
    {
        var httpContext = HttpContextFactory.Create();
        var problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Type = "caller-supplied" };

        ProblemDetailsDefaults.Normalise(problem, httpContext);

        problem.Type.ShouldBe("caller-supplied");
    }
}

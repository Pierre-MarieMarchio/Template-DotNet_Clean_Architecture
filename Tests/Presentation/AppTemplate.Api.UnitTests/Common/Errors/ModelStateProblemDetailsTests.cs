using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Common.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

/// <summary>
/// Exercises the same factory <c>Program.cs</c> wires into <c>ApiBehaviorOptions</c>, so a test here
/// is a statement about exactly what a caller who sends a malformed body receives.
/// </summary>
public sealed class ModelStateProblemDetailsTests
{
    [Fact]
    public void InvalidModelStateResponseFactory_AnswersTheSameGraphAsAnApplicationValidationFailure()
    {
        var httpContext = HttpContextFactory.Create();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Name", "The Name field is required.");

        ObjectResult bindingResult = CreateResponse(httpContext, modelState);

        var applicationDetails = new Dictionary<string, IReadOnlyList<string>>
        {
            ["name"] = ["is required"],
        };
        var applicationError = AppTemplate.Application.Common.Error.Validation(
            ValidationError.Code,
            "One or more fields are invalid.",
            applicationDetails);
        ObjectResult applicationResult = (ObjectResult)applicationError.ToActionResult(httpContext);

        bindingResult.StatusCode.ShouldBe(applicationResult.StatusCode);
        bindingResult.Value.ShouldBeOfType<ValidationProblemDetails>();
        applicationResult.Value.ShouldBeOfType<ValidationProblemDetails>();

        var bindingProblem = (ValidationProblemDetails)bindingResult.Value!;
        var applicationProblem = (ValidationProblemDetails)applicationResult.Value!;

        bindingProblem.Title.ShouldBe(applicationProblem.Title);
        bindingProblem.Extensions["code"].ShouldBe(applicationProblem.Extensions["code"]);
        bindingProblem.Extensions.ContainsKey("traceId").ShouldBeTrue();
        applicationProblem.Extensions.ContainsKey("traceId").ShouldBeTrue();
    }

    [Fact]
    public void InvalidModelStateResponseFactory_CamelCasesModelStateKeys()
    {
        var httpContext = HttpContextFactory.Create();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("TodoListId", "must be a valid id.");

        ObjectResult result = CreateResponse(httpContext, modelState);
        var problem = (ValidationProblemDetails)result.Value!;

        problem.Errors.Keys.ShouldContain("todoListId");
    }

    [Fact]
    public void InvalidModelStateResponseFactory_SetsTheProblemJsonContentType()
    {
        var httpContext = HttpContextFactory.Create();
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Name", "required");

        ObjectResult result = CreateResponse(httpContext, modelState);

        result.ContentTypes.ShouldContain("application/problem+json");
    }

    private static ObjectResult CreateResponse(HttpContext httpContext, ModelStateDictionary modelState)
    {
        var services = new ServiceCollection();
        services.AddApiModelStateProblemDetails();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IOptions<ApiBehaviorOptions>>().Value.InvalidModelStateResponseFactory;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor(), modelState);

        return (ObjectResult)factory(actionContext);
    }
}

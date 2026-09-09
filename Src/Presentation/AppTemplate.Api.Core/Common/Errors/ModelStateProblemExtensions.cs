using System.Text.Json;
using AppTemplate.Application.Core.Common.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.Core.Common.Errors;

/// <summary>
/// Makes a model-binding failure answer with the same graph as an application validation failure
/// (<see cref="ErrorMapping"/>): 400, <c>title = "Invalid request"</c>, per-field <c>errors</c>, and
/// the same <c>code</c>, <c>traceId</c> and <c>type</c> every other producer carries. Without this,
/// MVC's default factory answers a bare <c>ValidationProblemDetails</c> with the framework's own
/// title and no <c>code</c> or <c>traceId</c> at all — a different dialect for the one distinction
/// (malformed request body vs. failed business rule) a client should never have to make.
/// </summary>
internal static class ModelStateProblemExtensions
{
    public static IServiceCollection AddApiModelStateProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = CreateResponse);

        // Left on, the JSON input formatter copies a JsonException's text — the CLR type it was
        // binding and the byte offset it stopped at — into the model error, and out to the client.
        // Qualified because Microsoft.AspNetCore.Http.Json declares a JsonOptions of its own; a
        // controller's input formatter reads MVC's.
        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(
            options => options.AllowInputFormatterExceptionMessages = false);

        return services;
    }

    private static ObjectResult CreateResponse(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => NormalizeKey(entry.Key),
                entry => entry.Value!.Errors
                    // A deserialisation failure carries the exception and no text, so the fixed
                    // sentence stands in: what a caller can act on is the field, which is the key.
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage)
                        ? "The value is invalid."
                        : error.ErrorMessage)
                    .ToArray(),
                StringComparer.Ordinal);

        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid request",
        };

        // Same code an application validation failure carries: the two are one dialect, not two.
        problem.Extensions["code"] = ValidationError.Code;

        ProblemDetailsNormaliser.Normalise(problem, context.HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

    // Model-binding keys are not guaranteed camelCase: a route parameter or an [FromForm] field can
    // arrive PascalCase.
    private static string NormalizeKey(string key) =>
        string.Join('.', key.Split('.').Select(NormalizeSegment));

    private static string NormalizeSegment(string segment)
    {
        int bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);

        if (bracketIndex < 0)
        {
            return segment.Length == 0 ? segment : JsonNamingPolicy.CamelCase.ConvertName(segment);
        }

        string identifier = segment[..bracketIndex];
        string suffix = segment[bracketIndex..];

        return identifier.Length == 0 ? segment : JsonNamingPolicy.CamelCase.ConvertName(identifier) + suffix;
    }
}

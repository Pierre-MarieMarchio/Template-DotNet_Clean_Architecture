using System.Text.Json;
using AppTemplate.Application.Core.Common.Validation;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Common.Errors;

internal static class ModelStateProblemExtensions
{
    public static IServiceCollection AddApiModelStateProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = CreateResponse);

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

        problem.Extensions["code"] = ValidationError.Code;

        ProblemDetailsNormaliser.Normalise(problem, context.HttpContext);

        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }

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
